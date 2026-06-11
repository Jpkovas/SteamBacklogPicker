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

    private func write(_ content: String, to url: URL) throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try content.write(to: url, atomically: true, encoding: .utf8)
    }
}
