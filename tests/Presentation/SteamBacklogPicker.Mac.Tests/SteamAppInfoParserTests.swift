import Foundation
import XCTest
@testable import SteamBacklogPickerMac

final class SteamAppInfoParserTests: XCTestCase {
    func testParseAppMetadataReadsModernStringTableNamesAndTypes() throws {
        let appInfoURL = FileManager.default.temporaryDirectory
            .appendingPathComponent("SteamBacklogPickerAppInfo-\(UUID().uuidString).vdf")
        defer { try? FileManager.default.removeItem(at: appInfoURL) }

        try AppInfoFixtureFactory.makeAppInfoFixture(entries: [
            AppInfoFixtureEntry(
                appId: 550,
                name: "Left 4 Dead 2",
                type: "game",
                categoryIds: [2, 38],
                deckCategory: 2
            )
        ]).write(to: appInfoURL)

        let metadata = SteamAppInfoParser().parseAppMetadata(from: appInfoURL)

        XCTAssertEqual(metadata[550]?.name, "Left 4 Dead 2")
        XCTAssertEqual(metadata[550]?.type, "game")
        XCTAssertEqual(metadata[550]?.storeCategoryIds, [2, 38])
        XCTAssertEqual(metadata[550]?.deckCompatibility, .playable)
    }

    func testParseAppMetadataReadsFamilySharingFlag() throws {
        let appInfoURL = FileManager.default.temporaryDirectory
            .appendingPathComponent("SteamBacklogPickerAppInfo-\(UUID().uuidString).vdf")
        defer { try? FileManager.default.removeItem(at: appInfoURL) }

        try AppInfoFixtureFactory.makeAppInfoFixture(entries: [
            AppInfoFixtureEntry(
                appId: 620,
                name: "Portal 2",
                type: "game",
                isFamilyShared: true
            )
        ]).write(to: appInfoURL)

        let metadata = SteamAppInfoParser().parseAppMetadata(from: appInfoURL)

        XCTAssertEqual(metadata[620]?.isFamilyShared, true)
    }

}
