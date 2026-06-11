import Foundation

protocol SteamAppNameResolving {
    func resolveNames(for appIds: Set<UInt32>) -> [UInt32: String]
}

struct SteamAppNameResolver: SteamAppNameResolving {
    private let fileManager: FileManager
    private let cacheURL: URL
    private let endpointURL: URL

    init(
        fileManager: FileManager = .default,
        cacheURL: URL? = nil,
        endpointURL: URL = URL(string: "https://store.steampowered.com/api/appdetails")!
    ) {
        self.fileManager = fileManager
        self.endpointURL = endpointURL

        if let cacheURL {
            self.cacheURL = cacheURL
        } else {
            let supportDirectory = fileManager.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
                ?? fileManager.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support", isDirectory: true)
            self.cacheURL = supportDirectory
                .appendingPathComponent("SteamBacklogPicker", isDirectory: true)
                .appendingPathComponent("steam-app-names.json")
        }
    }

    func resolveNames(for appIds: Set<UInt32>) -> [UInt32: String] {
        guard !appIds.isEmpty else {
            return [:]
        }

        var cache = loadCache()
        var result = names(for: appIds, in: cache)
        let unresolved = appIds.subtracting(result.keys)
        guard !unresolved.isEmpty else {
            return result
        }

        guard let fetched = fetchAppDetails(matching: unresolved) else {
            return result
        }

        for (appId, name) in fetched {
            cache[appId] = name
            result[appId] = name
        }
        saveCache(cache)
        return result
    }

    private func names(for appIds: Set<UInt32>, in cache: [UInt32: String]) -> [UInt32: String] {
        Dictionary(uniqueKeysWithValues: appIds.compactMap { appId in
            guard let name = cache[appId], !name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
                return nil
            }
            return (appId, name)
        })
    }

    private func loadCache() -> [UInt32: String] {
        guard
            let data = try? Data(contentsOf: cacheURL),
            let raw = try? JSONDecoder().decode([String: String].self, from: data)
        else {
            return [:]
        }

        return Dictionary(uniqueKeysWithValues: raw.compactMap { key, value in
            UInt32(key).map { ($0, value) }
        })
    }

    private func saveCache(_ cache: [UInt32: String]) {
        let raw = Dictionary(uniqueKeysWithValues: cache.map { (String($0.key), $0.value) })
        guard let data = try? JSONEncoder().encode(raw) else {
            return
        }

        do {
            try fileManager.createDirectory(at: cacheURL.deletingLastPathComponent(), withIntermediateDirectories: true)
            try data.write(to: cacheURL, options: .atomic)
        } catch {
            // Name cache is an optimization; library loading must not fail if it cannot be written.
        }
    }

    private func fetchAppDetails(matching unresolved: Set<UInt32>) -> [UInt32: String]? {
        if endpointURL.isFileURL {
            guard let data = try? Data(contentsOf: endpointURL) else {
                return nil
            }
            return parseAppDetails(data: data, matching: unresolved)
        }

        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = 8
        configuration.timeoutIntervalForResource = 20
        let session = URLSession(configuration: configuration)
        let group = DispatchGroup()
        let lock = NSLock()
        var result: [UInt32: String] = [:]

        for appId in unresolved {
            guard let url = appDetailsURL(appId: appId) else {
                continue
            }

            group.enter()
            session.dataTask(with: url) { data, _, _ in
                defer { group.leave() }
                guard let data, let name = parseAppDetails(data: data, matching: [appId])?[appId] else {
                    return
                }

                lock.lock()
                result[appId] = name
                lock.unlock()
            }.resume()
        }

        _ = group.wait(timeout: .now() + 25)
        session.invalidateAndCancel()

        return result.isEmpty ? nil : result
    }

    private func appDetailsURL(appId: UInt32) -> URL? {
        var components = URLComponents(url: endpointURL, resolvingAgainstBaseURL: false)
        components?.queryItems = [
            URLQueryItem(name: "appids", value: String(appId)),
            URLQueryItem(name: "filters", value: "basic")
        ]
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
