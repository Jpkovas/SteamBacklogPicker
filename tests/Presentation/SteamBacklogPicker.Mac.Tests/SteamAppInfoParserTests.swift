import Foundation
import XCTest
import libzstd
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
                deckCategory: 2,
                osList: "windows,macos,linux"
            )
        ]).write(to: appInfoURL)

        let metadata = SteamAppInfoParser().parseAppMetadata(from: appInfoURL)

        XCTAssertEqual(metadata[550]?.name, "Left 4 Dead 2")
        XCTAssertEqual(metadata[550]?.type, "game")
        XCTAssertEqual(metadata[550]?.storeCategoryIds, [2, 38])
        XCTAssertEqual(metadata[550]?.deckCompatibility, .playable)
        XCTAssertEqual(metadata[550]?.supportedPlatforms, [.windows, .macOS, .linux])
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

    func testMalformedHeadersAndEveryTruncatedPrefixAreSafe() throws {
        let fixture = AppInfoFixtureFactory.makeAppInfoFixture(appId: 550, name: "Valid", type: "game")
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: url) }
        for length in 0..<fixture.count {
            try Data(fixture.prefix(length)).write(to: url)
            _ = SteamAppInfoParser().parseAppMetadata(from: url)
        }
        for offset in [UInt64.max, UInt64(Int.max), 0] {
            var corrupt = fixture
            corrupt.replaceSubrange(8..<16, with: littleEndian(offset, width: 8))
            try corrupt.write(to: url)
            XCTAssertTrue(SteamAppInfoParser().parseAppMetadata(from: url).isEmpty)
        }
        var oversized = fixture
        let tableOffset = Int(fixture[8..<16].enumerated().reduce(UInt64(0)) { $0 | UInt64($1.element) << ($1.offset * 8) })
        oversized.replaceSubrange(tableOffset..<(tableOffset + 4), with: [255, 255, 255, 255])
        try oversized.write(to: url)
        XCTAssertTrue(SteamAppInfoParser().parseAppMetadata(from: url).isEmpty)
    }

    func testReadsValveZstdAndRejectsIncorrectChecksum() throws {
        XCTAssertEqual(SteamAppInfoParser.crc32(Data("123456789".utf8)), 0xCBF43926)
        let fixture = AppInfoFixtureFactory.makeAppInfoFixture(appId: 550, name: "Compressed", type: "game")
        let oldTableOffset = Int(fixture[8..<16].enumerated().reduce(UInt64(0)) { $0 | UInt64($1.element) << ($1.offset * 8) })
        let payload = Data(fixture[84..<(oldTableOffset - 4)])
        var compressed = Data(count: ZSTD_compressBound(payload.count))
        let written = compressed.withUnsafeMutableBytes { output in
            payload.withUnsafeBytes { input in
                ZSTD_compress(output.baseAddress, output.count, input.baseAddress, input.count, 1)
            }
        }
        XCTAssertEqual(ZSTD_isError(written), 0)
        compressed.count = written
        let crc = SteamAppInfoParser.crc32(payload)
        var envelope = littleEndian(0x615A5356, width: 4)
        envelope.append(littleEndian(UInt64(crc), width: 4))
        envelope.append(compressed)
        envelope.append(littleEndian(UInt64(crc), width: 4))
        envelope.append(littleEndian(UInt64(payload.count), width: 4))
        envelope.append(Data(repeating: 0, count: 4))
        envelope.append(contentsOf: [0x7a, 0x73, 0x76])
        var encoded = Data(fixture.prefix(84))
        encoded.replaceSubrange(20..<24, with: littleEndian(UInt64(60 + envelope.count), width: 4))
        encoded.append(envelope)
        encoded.append(Data(repeating: 0, count: 4))
        let tableOffset = encoded.count
        encoded.append(fixture[oldTableOffset...])
        encoded.replaceSubrange(8..<16, with: littleEndian(UInt64(tableOffset), width: 8))
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: url) }
        try encoded.write(to: url)
        XCTAssertEqual(SteamAppInfoParser().parseAppMetadata(from: url)[550]?.name, "Compressed")
        encoded[88] ^= 1
        try encoded.write(to: url)
        XCTAssertTrue(SteamAppInfoParser().parseAppMetadata(from: url).isEmpty)
    }

    func testValveDeckCategoriesAndDlcRetainTheirMeaning() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: url) }
        try AppInfoFixtureFactory.makeAppInfoFixture(entries: [
            AppInfoFixtureEntry(appId: 1, name: "Unsupported", type: "game", deckCategory: 1),
            AppInfoFixtureEntry(appId: 3, name: "Verified", type: "game", deckCategory: 3)
        ]).write(to: url)
        let metadata = SteamAppInfoParser().parseAppMetadata(from: url)
        XCTAssertEqual(metadata[1]?.deckCompatibility, .unsupported)
        XCTAssertEqual(metadata[3]?.deckCompatibility, .verified)
        XCTAssertEqual(ProductCategory.fromSteamType("dlc"), .dlc)
    }

    func testFourByteAppIdFooterPreservesMetadataInSupportedVersions() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: url) }
        for version in 39...42 {
            let fixture: Data
            if version >= 41 {
                var modern = AppInfoFixtureFactory.makeAppInfoFixture(appId: 550, name: "Footer regression", type: "game")
                modern[0] = UInt8(version)
                fixture = modern
            } else {
                var payload = Data([0x00])
                payload.append(contentsOf: "common\0".utf8)
                payload.append(0x01)
                payload.append(contentsOf: "name\0Footer regression\0".utf8)
                payload.append(0x08)
                var legacy = littleEndian(UInt64((0x075644 << 8) | version), width: 4)
                legacy.append(littleEndian(1, width: 4))
                legacy.append(littleEndian(550, width: 4))
                let metadataSize = version >= 40 ? 60 : 40
                legacy.append(littleEndian(UInt64(metadataSize + payload.count), width: 4))
                legacy.append(Data(repeating: 0, count: metadataSize))
                legacy.append(payload)
                legacy.append(littleEndian(0, width: 4))
                fixture = legacy
            }
            try fixture.write(to: url)
            XCTAssertEqual(SteamAppInfoParser().parseAppMetadata(from: url)[550]?.name, "Footer regression", "version \(version)")
        }
    }

    func testInvalidDisplayEncodingDoesNotDiscardOtherMetadata() throws {
        var fixture = AppInfoFixtureFactory.makeAppInfoFixture(appId: 550, name: "Legacy display", type: "game")
        let nameRange = try XCTUnwrap(fixture.range(of: Data("Legacy display".utf8)))
        fixture[nameRange.lowerBound] = 0xFF
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: url) }
        try fixture.write(to: url)
        let metadata = SteamAppInfoParser().parseAppMetadata(from: url)
        XCTAssertEqual(metadata[550]?.type, "game")
        XCTAssertEqual(metadata[550]?.name, "\u{FFFD}egacy display")
    }

    private func littleEndian(_ value: UInt64, width: Int) -> Data {
        Data((0..<width).map { UInt8(truncatingIfNeeded: value >> ($0 * 8)) })
    }
}
