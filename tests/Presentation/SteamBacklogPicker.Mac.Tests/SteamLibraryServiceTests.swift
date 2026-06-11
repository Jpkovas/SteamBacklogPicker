import XCTest
@testable import SteamBacklogPickerMac

final class SteamLibraryServiceTests: XCTestCase {
    private var temporaryDirectory: URL!

    override func setUpWithError() throws {
        temporaryDirectory = FileManager.default.temporaryDirectory
            .appendingPathComponent("SteamBacklogPickerMacTests-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: temporaryDirectory, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        if let temporaryDirectory {
            try? FileManager.default.removeItem(at: temporaryDirectory)
        }
    }

    func testLoadLibraryIncludesInstalledAvailableLibraryCacheAndCollectionOnlyApps() throws {
        let steam = temporaryDirectory.appendingPathComponent("Steam", isDirectory: true)
        try createSteamFixture(at: steam)

        let games = try SteamLibraryService(
            steamDirectory: steam,
            appNameResolver: FixtureAppNameResolver(names: [50: "Resolved Remote Name"])
        ).loadLibrary()
        let byAppId = Dictionary(uniqueKeysWithValues: games.compactMap { game in
            game.steamAppId.map { ($0, game) }
        })

        XCTAssertEqual(Set(byAppId.keys), [10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120])
        XCTAssertEqual(byAppId[10]?.title, "Installed Game")
        XCTAssertEqual(byAppId[10]?.installState, .installed)
        XCTAssertEqual(byAppId[10]?.coverURL?.isFileURL, true)
        XCTAssertEqual(byAppId[10]?.coverURL?.lastPathComponent, "10_header.jpg")
        XCTAssertEqual(byAppId[10]?.coverURLs.first?.lastPathComponent, "10_header.jpg")
        XCTAssertTrue(byAppId[10]?.coverURLs.contains(URL(string: "https://cdn.cloudflare.steamstatic.com/steam/apps/10/header.jpg")!) == true)
        XCTAssertEqual(byAppId[20]?.title, "Available From AppInfo")
        XCTAssertEqual(byAppId[20]?.installState, .available)
        XCTAssertFalse(games.filter { $0.installState == .installed || $0.installState == .shared }.contains { $0.steamAppId == 20 })
        XCTAssertEqual(
            Set(games.filter { $0.installState == .installed || $0.installState == .shared }.compactMap(\.steamAppId)),
            [10, 100, 110]
        )
        XCTAssertEqual(byAppId[20]?.coverURLs.map(\.absoluteString), [
            "https://cdn.cloudflare.steamstatic.com/steam/apps/20/header.jpg",
            "https://cdn.cloudflare.steamstatic.com/steam/apps/20/capsule_616x353.jpg",
            "https://steamdb.info/static/cdn/steam/apps/20/header.jpg",
            "https://cdn.cloudflare.steamstatic.com/steam/apps/20/library_600x900.jpg"
        ])
        XCTAssertEqual(byAppId[20]?.storeCategoryIds, [2])
        XCTAssertEqual(byAppId[20]?.deckCompatibility, .playable)
        XCTAssertEqual(byAppId[30]?.title, "Available From Cache")
        XCTAssertEqual(byAppId[30]?.installState, .available)
        XCTAssertEqual(byAppId[40]?.title, "Collection From AppInfo")
        XCTAssertEqual(byAppId[40]?.productCategory, .software)
        XCTAssertTrue(byAppId[40]?.tags.contains("Favorites") == true)
        XCTAssertEqual(byAppId[50]?.title, "Resolved Remote Name")
        XCTAssertEqual(byAppId[60]?.productCategory, .soundtrack)
        XCTAssertEqual(byAppId[70]?.productCategory, .tool)
        XCTAssertEqual(byAppId[80]?.productCategory, .video)
        XCTAssertEqual(byAppId[90]?.productCategory, .other)
        XCTAssertEqual(byAppId[100]?.title, "Shared Installed")
        XCTAssertEqual(byAppId[100]?.installState, .shared)
        XCTAssertEqual(byAppId[100]?.ownershipType, .familyShared)
        XCTAssertEqual(byAppId[110]?.title, "Shared Available")
        XCTAssertEqual(byAppId[110]?.installState, .shared)
        XCTAssertEqual(byAppId[110]?.ownershipType, .familyShared)
        XCTAssertEqual(byAppId[120]?.title, "Available From AppInfo")
        XCTAssertLessThan(
            games.firstIndex { $0.steamAppId == 120 }!,
            games.firstIndex { $0.steamAppId == 20 }!
        )
        XCTAssertTrue(byAppId[20]?.tags.contains("Backlog") == true)
        XCTAssertTrue(byAppId[20]?.tags.contains("Cloud Favorites") == true)
        XCTAssertTrue(byAppId[20]?.tags.contains("Deck Ready") == true)
        XCTAssertTrue(byAppId[20]?.tags.contains("Single Player") == true)
        XCTAssertFalse(byAppId[20]?.tags.contains("Installed Dynamic") == true)
        XCTAssertTrue(byAppId[10]?.tags.contains("Installed Dynamic") == true)
        XCTAssertTrue(byAppId[100]?.tags.contains("Installed Dynamic") == true)
        XCTAssertTrue(byAppId[110]?.tags.contains("Installed Dynamic") == true)
        XCTAssertFalse(byAppId[20]?.tags.contains("Installed With Empty Group") == true)
        XCTAssertTrue(byAppId[10]?.tags.contains("Installed With Empty Group") == true)
        XCTAssertTrue(byAppId[100]?.tags.contains("Installed With Empty Group") == true)
        XCTAssertTrue(byAppId[110]?.tags.contains("Installed With Empty Group") == true)
        XCTAssertFalse(byAppId[10]?.tags.contains("All Empty Groups") == true)
        XCTAssertFalse(byAppId[20]?.tags.contains("String Encoded Collection") == true)
        XCTAssertFalse(byAppId[10]?.tags.contains("String Encoded Collection") == true)
        XCTAssertFalse(byAppId[20]?.tags.contains("All Empty Groups") == true)
        XCTAssertFalse(byAppId[110]?.tags.contains("All Empty Groups") == true)
    }

    private func createSteamFixture(at steam: URL) throws {
        try write(
            #"""
            "LibraryFolders"
            {
                "0"
                {
                    "path" "\#(steam.path)"
                }
            }
            """#,
            to: steam.appendingPathComponent("steamapps/libraryfolders.vdf")
        )

        try write(
            #"""
            "AppState"
            {
                "appid" "10"
                "name" "Wrong Manifest Label"
                "SizeOnDisk" "100"
            }
            """#,
            to: steam.appendingPathComponent("steamapps/appmanifest_10.acf")
        )
        try write(
            #"""
            "AppState"
            {
                "appid" "100"
                "name" "Shared Installed"
                "LastOwner" "76561198000000001"
            }
            """#,
            to: steam.appendingPathComponent("steamapps/appmanifest_100.acf")
        )
        try writeData(
            Data([0xff, 0xd8, 0xff, 0xd9]),
            to: steam.appendingPathComponent("appcache/librarycache/10_header.jpg")
        )

        try write(
            #"""
            "users"
            {
                "76561198000000000"
                {
                    "MostRecent" "1"
                    "Timestamp" "1710000000"
                }
            }
            """#,
            to: steam.appendingPathComponent("config/loginusers.vdf")
        )

        try write(
            #"""
            "UserLocalConfigStore"
            {
                "Software"
                {
                    "Valve"
                    {
                        "Steam"
                        {
                            "apps"
                            {
                                "20"
                                {
                                    "name" "Wrong Local Label"
                                    "Installed" "1"
                                    "AppType" "game"
                                }
                                "50"
                                {
                                    "AppType" "game"
                                }
                                "60"
                                {
                                    "AppType" "game"
                                }
                                "70"
                                {
                                    "AppType" "game"
                                }
                                "80"
                                {
                                    "AppType" "game"
                                }
                                "90"
                                {
                                    "AppType" "game"
                                }
                                "110"
                                {
                                    "name" "Shared Available"
                                    "AppType" "game"
                                    "IsSubscribedFromFamilySharing" "1"
                                }
                                "120"
                                {
                                    "name" "Duplicate Local Label"
                                    "AppType" "game"
                                }
                            }
                        }
                    }
                }
            }
            """#,
            to: steam.appendingPathComponent("userdata/76561198000000000/config/localconfig.vdf")
        )

        try write(
            #"{"name":"Available From Cache","app_type":"game","is_installed":false}"#,
            to: steam.appendingPathComponent("userdata/76561198000000000/config/librarycache/30.json")
        )

        try write(
            #"""
            "UserRoamingConfigStore"
            {
                "tags"
                {
                    "1" "Backlog"
                }
                "apps"
                {
                    "20"
                    {
                        "tags"
                        {
                            "1" "1"
                        }
                    }
                }
                "collections"
                {
                    "0"
                    {
                        "display_name" "Favorites"
                        "apps"
                        {
                            "40" ""
                        }
                    }
                }
            }
            """#,
            to: steam.appendingPathComponent("userdata/76561198000000000/7/remote/sharedconfig.vdf")
        )

        try writeAppInfoFixture(
            entries: [
                AppInfoFixtureEntry(appId: 10, name: "Installed Game", type: "game"),
                AppInfoFixtureEntry(
                    appId: 20,
                    name: "Available From AppInfo",
                    type: "game",
                    categoryIds: [2],
                    deckCategory: 2
                ),
                AppInfoFixtureEntry(appId: 40, name: "Collection From AppInfo", type: "software"),
                AppInfoFixtureEntry(appId: 60, name: "Fixture Soundtrack", type: "soundtrack"),
                AppInfoFixtureEntry(appId: 70, name: "Fixture Tool", type: "tool"),
                AppInfoFixtureEntry(appId: 80, name: "Fixture Video", type: "video"),
                AppInfoFixtureEntry(appId: 90, name: "Fixture Other", type: "dlc"),
                AppInfoFixtureEntry(appId: 100, name: "Shared Installed", type: "game"),
                AppInfoFixtureEntry(appId: 110, name: "Shared Available", type: "game"),
                AppInfoFixtureEntry(appId: 120, name: "Available From AppInfo", type: "game")
            ],
            to: steam.appendingPathComponent("appcache/appinfo.vdf")
        )

        try writeCloudCollections(
            to: steam.appendingPathComponent("userdata/76561198000000000/config/cloudstorage/cloud-storage-namespace-1.json")
        )
    }

    private func write(_ content: String, to url: URL) throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try content.write(to: url, atomically: true, encoding: .utf8)
    }

    private func writeData(_ data: Data, to url: URL) throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try data.write(to: url)
    }

    private func writeAppInfoFixture(entries: [AppInfoFixtureEntry], to url: URL) throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try AppInfoFixtureFactory.makeAppInfoFixture(entries: entries).write(to: url)
    }

    private func writeCloudCollections(to url: URL) throws {
        let entries: [[Any]] = [
            makeCloudCollection(
                id: "installed",
                name: "Installed Dynamic",
                added: [],
                options: [1],
                timestamp: 10
            ),
            makeCloudCollectionWithGroups(
                id: "installed-empty-group",
                name: "Installed With Empty Group",
                added: [],
                groups: [
                    (options: [], acceptUnion: true),
                    (options: [1], acceptUnion: false)
                ],
                timestamp: 15
            ),
            makeCloudCollectionWithGroups(
                id: "all-empty-groups",
                name: "All Empty Groups",
                added: [],
                groups: [
                    (options: [], acceptUnion: true),
                    (options: [], acceptUnion: false)
                ],
                timestamp: 16
            ),
            makeCloudCollection(
                id: "single-player",
                name: "Single Player",
                added: [],
                options: [7],
                timestamp: 20
            ),
            makeCloudCollection(
                id: "deck-ready",
                name: "Deck Ready",
                added: [],
                options: [13],
                timestamp: 30
            ),
            makeCloudCollection(
                id: "favorites",
                name: "Cloud Favorites",
                added: [20],
                options: [],
                timestamp: 40
            ),
            makeRawCloudCollection(
                id: "string-encoded",
                name: "String Encoded Collection",
                added: ["20"],
                groups: [
                    [
                        "rgOptions": ["1"],
                        "bAcceptUnion": false
                    ]
                ],
                timestamp: 50
            )
        ]

        let data = try JSONSerialization.data(withJSONObject: entries, options: [])
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try data.write(to: url)
    }

    private func makeCloudCollection(
        id: String,
        name: String,
        added: [UInt32],
        options: [Int],
        timestamp: Int
    ) -> [Any] {
        var collection: [String: Any] = [
            "id": id,
            "name": name,
            "added": added.map(Int.init),
            "removed": []
        ]

        if !options.isEmpty {
            collection["filterSpec"] = [
                "nFormatVersion": 2,
                "strSearchText": "",
                "filterGroups": [
                    [
                        "rgOptions": options,
                        "bAcceptUnion": false
                    ]
                ]
            ]
        }

        let valueData = try! JSONSerialization.data(withJSONObject: collection, options: [])
        let value = String(data: valueData, encoding: .utf8)!
        return [
            "user-collections.\(id)",
            [
                "key": "user-collections.\(id)",
                "timestamp": timestamp,
                "value": value,
                "version": "1"
            ]
        ]
    }

    private func makeCloudCollectionWithGroups(
        id: String,
        name: String,
        added: [UInt32],
        groups: [(options: [Int], acceptUnion: Bool)],
        timestamp: Int
    ) -> [Any] {
        var collection: [String: Any] = [
            "id": id,
            "name": name,
            "added": added.map(Int.init),
            "removed": []
        ]

        collection["filterSpec"] = [
            "nFormatVersion": 2,
            "strSearchText": "",
            "filterGroups": groups.map { group in
                [
                    "rgOptions": group.options,
                    "bAcceptUnion": group.acceptUnion
                ] as [String: Any]
            }
        ]

        let valueData = try! JSONSerialization.data(withJSONObject: collection, options: [])
        let value = String(data: valueData, encoding: .utf8)!
        return [
            "user-collections.\(id)",
            [
                "key": "user-collections.\(id)",
                "timestamp": timestamp,
                "value": value,
                "version": "1"
            ]
        ]
    }

    private func makeRawCloudCollection(
        id: String,
        name: String,
        added: [Any],
        groups: [[String: Any]],
        timestamp: Int
    ) -> [Any] {
        let collection: [String: Any] = [
            "id": id,
            "name": name,
            "added": added,
            "removed": [],
            "filterSpec": [
                "nFormatVersion": 2,
                "strSearchText": "",
                "filterGroups": groups
            ]
        ]

        let valueData = try! JSONSerialization.data(withJSONObject: collection, options: [])
        let value = String(data: valueData, encoding: .utf8)!
        return [
            "user-collections.\(id)",
            [
                "key": "user-collections.\(id)",
                "timestamp": timestamp,
                "value": value,
                "version": "1"
            ]
        ]
    }
}

private struct FixtureAppNameResolver: SteamAppNameResolving {
    var names: [UInt32: String]

    func resolveNames(for appIds: Set<UInt32>) -> [UInt32: String] {
        Dictionary(uniqueKeysWithValues: appIds.compactMap { appId in
            names[appId].map { (appId, $0) }
        })
    }
}
