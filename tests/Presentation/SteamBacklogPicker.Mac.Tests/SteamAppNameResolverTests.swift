import XCTest
@testable import SteamBacklogPickerMac

final class SteamAppNameResolverTests: XCTestCase {
    private var temporaryDirectory: URL!

    override func setUpWithError() throws {
        temporaryDirectory = FileManager.default.temporaryDirectory
            .appendingPathComponent("SteamBacklogPickerNameResolverTests-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: temporaryDirectory, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        if let temporaryDirectory {
            try? FileManager.default.removeItem(at: temporaryDirectory)
        }
    }

    func testResolveNamesFetchesMatchingAppsAndCachesThem() throws {
        let endpointURL = temporaryDirectory.appendingPathComponent("applist.json")
        let cacheURL = temporaryDirectory.appendingPathComponent("steam-app-names.json")
        try write(
            """
            {
              "253710": {
                "success": true,
                "data": { "name": "theHunter Classic" }
              },
              "550": {
                "success": true,
                "data": { "name": "Left 4 Dead 2" }
              }
            }
            """,
            to: endpointURL
        )
        let resolver = SteamAppNameResolver(cacheURL: cacheURL, endpointURL: endpointURL)

        XCTAssertEqual(resolver.resolveNames(for: [253710, 999999]), [253710: "theHunter Classic"])

        try FileManager.default.removeItem(at: endpointURL)

        XCTAssertEqual(resolver.resolveNames(for: [253710]), [253710: "theHunter Classic"])
    }

    func testCacheIsScopedByLanguageAndExpires() throws {
        let endpoint = temporaryDirectory.appendingPathComponent("names.json")
        let cache = temporaryDirectory.appendingPathComponent("cache.json")
        let initial = Date(timeIntervalSince1970: 1_700_000_000)
        try write(#"{"10":{"success":true,"data":{"name":"English"}}}"#, to: endpoint)
        let english = SteamAppNameResolver(cacheURL: cache, endpointURL: endpoint, language: "english", cacheTTL: 60, now: { initial })
        XCTAssertEqual(english.resolveNames(for: [10])[10], "English")
        try write(#"{"10":{"success":true,"data":{"name":"Português"}}}"#, to: endpoint)
        let portuguese = SteamAppNameResolver(cacheURL: cache, endpointURL: endpoint, language: "brazilian", cacheTTL: 60, now: { initial })
        XCTAssertEqual(portuguese.resolveNames(for: [10])[10], "Português")
        XCTAssertEqual(english.resolveNames(for: [10])[10], "English")
        let expired = SteamAppNameResolver(cacheURL: cache, endpointURL: endpoint, language: "english", cacheTTL: 60, now: { initial.addingTimeInterval(61) })
        XCTAssertEqual(expired.resolveNames(for: [10])[10], "Português")
    }

    func testCancelledResolutionDoesNotFetchOrCreateCache() throws {
        let endpoint = temporaryDirectory.appendingPathComponent("names.json")
        let cache = temporaryDirectory.appendingPathComponent("cancelled-cache.json")
        try write(#"{"10":{"success":true,"data":{"name":"Name"}}}"#, to: endpoint)
        let resolver = SteamAppNameResolver(cacheURL: cache, endpointURL: endpoint, isCancelled: { true })
        XCTAssertTrue(resolver.resolveNames(for: [10]).isEmpty)
        XCTAssertFalse(FileManager.default.fileExists(atPath: cache.path))
    }

    func testNetworkRequestsAreBoundedAndCancelledAtDeadline() {
        NameResolverURLProtocol.reset()
        let cache = temporaryDirectory.appendingPathComponent("network-cache.json")
        let resolver = SteamAppNameResolver(cacheURL: cache, timeout: 0.2, maximumConcurrentRequests: 2,
            sessionConfiguration: {
                let configuration = URLSessionConfiguration.ephemeral
                configuration.protocolClasses = [NameResolverURLProtocol.self]
                return configuration
            })
        let started = Date()
        XCTAssertTrue(resolver.resolveNames(for: Set(1...30)).isEmpty)
        XCTAssertLessThan(Date().timeIntervalSince(started), 2)
        XCTAssertLessThanOrEqual(NameResolverURLProtocol.maximumActive, 2)
        XCTAssertGreaterThan(NameResolverURLProtocol.maximumActive, 0)
    }

    private func write(_ content: String, to url: URL) throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try content.write(to: url, atomically: true, encoding: .utf8)
    }
}

private final class NameResolverURLProtocol: URLProtocol {
    private static let lock = NSLock()
    private static var active = 0
    private static var peak = 0
    private var started = false

    static var maximumActive: Int {
        lock.lock()
        defer { lock.unlock() }
        return peak
    }

    static func reset() {
        lock.lock()
        defer { lock.unlock() }
        active = 0
        peak = 0
    }

    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }
    override func startLoading() {
        Self.lock.lock()
        defer { Self.lock.unlock() }
        started = true
        Self.active += 1
        Self.peak = max(Self.peak, Self.active)
        // Deliberately never complete: the resolver must cancel the session at its deadline.
    }
    override func stopLoading() {
        Self.lock.lock()
        defer { Self.lock.unlock() }
        if started { Self.active -= 1; started = false }
    }
}
