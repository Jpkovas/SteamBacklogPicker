import Foundation

protocol SteamAppNameResolving {
    func resolveNames(for appIds: Set<UInt32>) -> [UInt32: String]
}

struct SteamAppNameResolver: SteamAppNameResolving {
    private let fileManager: FileManager
    private let cacheURL: URL
    private let endpointURL: URL
    private let language: String
    private let cacheTTL: TimeInterval
    private let now: () -> Date
    private let isCancelled: () -> Bool
    private let sessionConfiguration: () -> URLSessionConfiguration
    private let timeout: TimeInterval
    private let maximumConcurrentRequests: Int

    init(fileManager: FileManager = .default, cacheURL: URL? = nil,
         endpointURL: URL = URL(string: "https://store.steampowered.com/api/appdetails")!,
         language: String = "english", cacheTTL: TimeInterval = 7 * 24 * 60 * 60,
         now: @escaping () -> Date = Date.init, isCancelled: @escaping () -> Bool = { false },
         timeout: TimeInterval = 25, maximumConcurrentRequests: Int = 4,
         sessionConfiguration: @escaping () -> URLSessionConfiguration = { .ephemeral }) {
        self.fileManager = fileManager
        self.endpointURL = endpointURL
        self.language = language.lowercased()
        self.cacheTTL = max(0, cacheTTL)
        self.now = now
        self.isCancelled = isCancelled
        self.timeout = max(0, timeout)
        self.maximumConcurrentRequests = max(1, min(8, maximumConcurrentRequests))
        self.sessionConfiguration = sessionConfiguration
        let support = fileManager.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? fileManager.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support")
        self.cacheURL = cacheURL ?? support.appendingPathComponent("SteamBacklogPicker/steam-app-names.json")
    }

    private struct CacheEntry: Codable {
        let name: String
        let language: String
        let fetchedAt: Date
    }

    func resolveNames(for appIds: Set<UInt32>) -> [UInt32: String] {
        guard !appIds.isEmpty, !isCancelled() else { return [:] }
        let timestamp = now()
        var cache = loadCache().filter { _, entry in
            let age = timestamp.timeIntervalSince(entry.fetchedAt)
            return age >= 0 && age < cacheTTL
        }
        var result: [UInt32: String] = [:]
        for appId in appIds {
            if let entry = cache[cacheKey(appId)], entry.language == language { result[appId] = entry.name }
        }
        let unresolved = appIds.subtracting(result.keys)
        guard !unresolved.isEmpty else { return result }
        let fetched = fetchAppDetails(matching: unresolved)
        for (appId, name) in fetched {
            result[appId] = name
            cache[cacheKey(appId)] = CacheEntry(name: name, language: language, fetchedAt: timestamp)
        }
        if !fetched.isEmpty { saveCache(cache) }
        return result
    }

    private func cacheKey(_ appId: UInt32) -> String { "\(language):\(appId)" }

    private func loadCache() -> [String: CacheEntry] {
        guard let data = try? Data(contentsOf: cacheURL),
              data.count <= 16 * 1024 * 1024,
              let cache = try? JSONDecoder().decode([String: CacheEntry].self, from: data) else { return [:] }
        return cache
    }

    private func saveCache(_ cache: [String: CacheEntry]) {
        guard let data = try? JSONEncoder().encode(cache) else { return }
        do {
            try fileManager.createDirectory(at: cacheURL.deletingLastPathComponent(), withIntermediateDirectories: true)
            try data.write(to: cacheURL, options: .atomic)
        } catch {
            // A cache write failure must not prevent library loading.
        }
    }

    private func fetchAppDetails(matching unresolved: Set<UInt32>) -> [UInt32: String] {
        if endpointURL.isFileURL {
            guard !isCancelled(), let data = try? Data(contentsOf: endpointURL) else { return [:] }
            return parseAppDetails(data: data, matching: unresolved) ?? [:]
        }
        let configuration = sessionConfiguration()
        configuration.timeoutIntervalForRequest = min(8, timeout)
        configuration.timeoutIntervalForResource = timeout
        let session = URLSession(configuration: configuration)
        defer { session.invalidateAndCancel() }
        let work = NameResolutionWork(appIds: unresolved.sorted())
        let queue = OperationQueue()
        queue.maxConcurrentOperationCount = maximumConcurrentRequests
        let deadline = DispatchTime.now() + timeout
        for _ in 0..<maximumConcurrentRequests {
            queue.addOperation {
                while !isCancelled(), DispatchTime.now() < deadline, let appId = work.next() {
                    guard let url = appDetailsURL(appId: appId) else { continue }
                    let completed = DispatchSemaphore(value: 0)
                    let task = session.dataTask(with: url) { data, response, error in
                        defer { completed.signal() }
                        guard error == nil, let response = response as? HTTPURLResponse,
                              (200..<300).contains(response.statusCode), let data,
                              data.count <= 2 * 1024 * 1024,
                              let name = parseAppDetails(data: data, matching: [appId])?[appId] else { return }
                        work.record(appId: appId, name: name)
                    }
                    task.resume()
                    while !isCancelled(), DispatchTime.now() < deadline {
                        if completed.wait(timeout: min(deadline, .now() + 0.1)) == .success { break }
                    }
                    task.cancel()
                }
            }
        }
        queue.waitUntilAllOperationsAreFinished()
        // Close under the same lock used by callbacks before returning an immutable snapshot.
        return work.finish()
    }

    private func appDetailsURL(appId: UInt32) -> URL? {
        var components = URLComponents(url: endpointURL, resolvingAgainstBaseURL: false)
        components?.queryItems = [URLQueryItem(name: "appids", value: String(appId)),
                                  URLQueryItem(name: "filters", value: "basic"),
                                  URLQueryItem(name: "l", value: language)]
        return components?.url
    }

    private func parseAppDetails(data: Data, matching unresolved: Set<UInt32>) -> [UInt32: String]? {
        guard
            let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
        else {
            return nil
        }

        var result: [UInt32: String] = [:]
        for appId in unresolved {
            guard
                let app = json[String(appId)] as? [String: Any],
                ((app["success"] as? Bool) ?? (app["success"] as? NSNumber)?.boolValue) == true,
                let data = app["data"] as? [String: Any]
            else {
                continue
            }

            let name = (data["name"] as? String)?.trimmingCharacters(in: .whitespacesAndNewlines)
            if let name, !name.isEmpty {
                result[appId] = name
            }
        }

        return result.isEmpty ? nil : result
    }
}

private final class NameResolutionWork: @unchecked Sendable {
    private let lock = NSLock()
    private let appIds: [UInt32]
    private var position = 0
    private var result: [UInt32: String] = [:]
    private var closed = false

    init(appIds: [UInt32]) { self.appIds = appIds }

    func next() -> UInt32? {
        lock.lock()
        defer { lock.unlock() }
        guard !closed, position < appIds.count else { return nil }
        defer { position += 1 }
        return appIds[position]
    }

    func record(appId: UInt32, name: String) {
        lock.lock()
        defer { lock.unlock() }
        if !closed { result[appId] = name }
    }

    func finish() -> [UInt32: String] {
        lock.lock()
        defer { lock.unlock() }
        closed = true
        return result
    }
}
