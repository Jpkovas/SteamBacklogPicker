import Foundation

struct AppInfoFixtureEntry {
    var appId: UInt32
    var name: String
    var type: String
    var isFamilyShared: Bool = false
    var categoryIds: [Int] = []
    var deckCategory: Int?
    var osList: String?
}

enum AppInfoFixtureFactory {
    static func makeAppInfoFixture(appId: UInt32, name: String, type: String) -> Data {
        makeAppInfoFixture(entries: [AppInfoFixtureEntry(appId: appId, name: name, type: type)])
    }

    static func makeAppInfoFixture(entries: [(appId: UInt32, name: String, type: String)]) -> Data {
        makeAppInfoFixture(entries: entries.map {
            AppInfoFixtureEntry(appId: $0.appId, name: $0.name, type: $0.type)
        })
    }

    static func makeAppInfoFixture(entries: [AppInfoFixtureEntry]) -> Data {
        let strings = makeStringTable(for: entries)

        var data = Data()
        data.appendUInt32((0x075644 << 8) | 41)
        data.appendUInt32(1)
        let stringTableOffsetPosition = data.count
        data.appendInt64(0)

        for entry in entries {
            let payload = makePayload(entry: entry, strings: strings)
            data.appendUInt32(entry.appId)
            data.appendUInt32(UInt32(60 + payload.count))
            data.append(Data(repeating: 0, count: 60))
            data.append(payload)
        }

        data.appendUInt32(0)

        let stringTableOffset = data.count
        data.appendUInt32(UInt32(strings.count))
        for string in strings {
            data.appendNullTerminatedString(string)
        }

        data.replaceSubrange(
            stringTableOffsetPosition..<stringTableOffsetPosition + 8,
            with: Data.littleEndianInt64(Int64(stringTableOffset))
        )
        return data
    }

    private static func makeStringTable(for entries: [AppInfoFixtureEntry]) -> [String] {
        var strings = [
            "appinfo",
            "appid",
            "public_only",
            "common",
            "name",
            "type",
            "category",
            "oslist",
            "steam_deck_compatibility",
            "overall_category",
            "IsSubscribedFromFamilySharing"
        ]

        for entry in entries {
            for categoryId in entry.categoryIds {
                let key = "category_\(categoryId)"
                if !strings.contains(key) {
                    strings.append(key)
                }
            }
        }

        return strings
    }

    private static func makePayload(entry: AppInfoFixtureEntry, strings: [String]) -> Data {
        var payload = Data()
        payload.append(0x00)
        payload.appendUInt32(index("appinfo", in: strings))
        payload.append(0x00)
        payload.appendUInt32(index("common", in: strings))
        payload.append(0x01)
        payload.appendUInt32(index("name", in: strings))
        payload.appendNullTerminatedString(entry.name)
        payload.append(0x01)
        payload.appendUInt32(index("type", in: strings))
        payload.appendNullTerminatedString(entry.type)
        if let osList = entry.osList {
            payload.append(0x01)
            payload.appendUInt32(index("oslist", in: strings))
            payload.appendNullTerminatedString(osList)
        }
        if entry.isFamilyShared {
            payload.append(0x02)
            payload.appendUInt32(index("IsSubscribedFromFamilySharing", in: strings))
            payload.appendUInt32(1)
        }

        if !entry.categoryIds.isEmpty {
            payload.append(0x00)
            payload.appendUInt32(index("category", in: strings))
            for categoryId in entry.categoryIds {
                payload.append(0x02)
                payload.appendUInt32(index("category_\(categoryId)", in: strings))
                payload.appendUInt32(1)
            }
            payload.append(0x08)
        }

        if let deckCategory = entry.deckCategory {
            payload.append(0x00)
            payload.appendUInt32(index("steam_deck_compatibility", in: strings))
            payload.append(0x02)
            payload.appendUInt32(index("overall_category", in: strings))
            payload.appendUInt32(UInt32(deckCategory))
            payload.append(0x08)
        }

        payload.append(0x08)
        payload.append(0x08)
        return payload
    }

    private static func index(_ value: String, in strings: [String]) -> UInt32 {
        UInt32(strings.firstIndex(of: value)!)
    }

    static func makeLegacyCommonFixture(appId: UInt32, name: String, type: String) -> Data {
        var payload = Data()
        payload.append(0x00)
        payload.appendUInt32(1)
        payload.append(0x01)
        payload.appendUInt32(2)
        payload.appendNullTerminatedString(name)
        payload.append(0x01)
        payload.appendUInt32(3)
        payload.appendNullTerminatedString(type)
        payload.append(0x08)

        var data = Data()
        data.appendUInt32((0x075644 << 8) | 41)
        data.appendUInt32(1)
        let stringTableOffsetPosition = data.count
        data.appendInt64(0)

        data.appendUInt32(appId)
        data.appendUInt32(UInt32(60 + payload.count))
        data.append(Data(repeating: 0, count: 60))
        data.append(payload)
        data.appendUInt32(0)

        let stringTableOffset = data.count
        data.appendUInt32(4)
        data.appendNullTerminatedString("")
        data.appendNullTerminatedString("common")
        data.appendNullTerminatedString("name")
        data.appendNullTerminatedString("type")

        data.replaceSubrange(
            stringTableOffsetPosition..<stringTableOffsetPosition + 8,
            with: Data.littleEndianInt64(Int64(stringTableOffset))
        )
        return data
    }
}

private extension Data {
    mutating func appendUInt32(_ value: UInt32) {
        append(UInt8(value & 0xFF))
        append(UInt8((value >> 8) & 0xFF))
        append(UInt8((value >> 16) & 0xFF))
        append(UInt8((value >> 24) & 0xFF))
    }

    mutating func appendInt64(_ value: Int64) {
        append(Self.littleEndianInt64(value))
    }

    mutating func appendNullTerminatedString(_ value: String) {
        append(contentsOf: value.data(using: .utf8) ?? Data())
        append(0)
    }

    static func littleEndianInt64(_ value: Int64) -> Data {
        let unsigned = UInt64(bitPattern: value)
        var data = Data()
        for shift in stride(from: 0, to: 64, by: 8) {
            data.append(UInt8((unsigned >> UInt64(shift)) & 0xFF))
        }
        return data
    }
}
