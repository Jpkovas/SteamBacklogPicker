import Foundation

struct SteamAppInfoParser {
    private static let expectedMagic: UInt32 = 0x075644
    private static let valveZstdMagic: UInt32 = 0x615A5356

    func parseAppMetadata(from url: URL) -> [UInt32: SteamAppInfoMetadata] {
        guard let data = try? Data(contentsOf: url), data.count >= 16 else {
            return [:]
        }

        let rawMagic = data.readUInt32(at: 0)
        let version = UInt8(rawMagic & 0xFF)
        let magic = rawMagic >> 8
        guard magic == Self.expectedMagic, (39...42).contains(version) else {
            return [:]
        }

        var offset = 8
        var stringTable: [String] = []
        if version >= 41 {
            let stringTableOffset = Int(data.readInt64(at: offset))
            offset += 8
            stringTable = readStringTable(data: data, offset: stringTableOffset)
        }

        var result: [UInt32: SteamAppInfoMetadata] = [:]
        while offset + 8 <= data.count {
            let appId = data.readUInt32(at: offset)
            let size = Int(data.readUInt32(at: offset + 4))
            offset += 8

            if appId == 0 && size == 0 {
                break
            }

            let entryEnd = offset + size
            guard size >= 0, entryEnd <= data.count else {
                break
            }

            let metadataLength = version >= 40 ? 60 : 40
            let payloadOffset = offset + metadataLength
            if payloadOffset <= entryEnd {
                let payload = Data(data[payloadOffset..<entryEnd])
                if let parsed = parsePayload(payload, appId: appId, stringTable: stringTable) {
                    result[appId] = parsed
                }
            }

            offset = entryEnd
        }

        return result
    }

    private func readStringTable(data: Data, offset: Int) -> [String] {
        guard offset + 4 <= data.count else {
            return []
        }

        let count = Int(data.readUInt32(at: offset))
        var cursor = offset + 4
        var strings: [String] = []
        strings.reserveCapacity(count)

        for _ in 0..<count {
            let (value, next) = data.readNullTerminatedString(at: cursor)
            strings.append(value)
            cursor = next
            if cursor >= data.count {
                break
            }
        }

        return strings
    }

    private func parsePayload(_ payload: Data, appId: UInt32, stringTable: [String]) -> SteamAppInfoMetadata? {
        guard payload.count >= 8 else {
            return nil
        }

        if payload.readUInt32(at: 0) == Self.valveZstdMagic {
            return nil
        }

        var cursor = 0
        let root = parseObject(payload, cursor: &cursor, stringTable: stringTable)
        let common = root.path("appinfo", "common")
            ?? root.children["common"]
            ?? root.firstDescendant(named: "common")
        let name = common?.children["name"]?.value
        let type = common?.children["type"]?.value
        let categoryIds = extractCategoryIds(from: common)
        let deckCompatibility = extractDeckCompatibility(from: common)
        let isFamilyShared = extractFamilySharingFlag(from: root) ?? false

        if name == nil && type == nil && categoryIds.isEmpty && deckCompatibility == .unknown && !isFamilyShared {
            return nil
        }

        return SteamAppInfoMetadata(
            appId: appId,
            name: name,
            type: type,
            storeCategoryIds: categoryIds,
            deckCompatibility: deckCompatibility,
            isFamilyShared: isFamilyShared
        )
    }

    private func extractCategoryIds(from common: AppInfoNode?) -> [Int] {
        guard let category = common?.children["category"] else {
            return []
        }

        return category.children.compactMap { key, node in
            guard key.hasPrefix("category_"),
                  let id = Int(key.dropFirst("category_".count)),
                  node.boolValue != false
            else {
                return nil
            }

            return id
        }
        .sorted()
    }

    private func extractDeckCompatibility(from common: AppInfoNode?) -> SteamDeckCompatibility {
        guard let deck = common?.children["steam_deck_compatibility"] else {
            return .unknown
        }

        let rawValue = deck.children["category"]?.value ?? deck.children["overall_category"]?.value
        switch rawValue.flatMap(Int.init) {
        case 1:
            return .verified
        case 2:
            return .playable
        case 3:
            return .unsupported
        default:
            return .unknown
        }
    }

    private func extractFamilySharingFlag(from node: AppInfoNode) -> Bool? {
        for (key, child) in node.children {
            if normalizedFlagName(key) == "issubscribedfromfamilysharing", let boolValue = child.boolValue {
                return boolValue
            }

            if let descendant = extractFamilySharingFlag(from: child) {
                return descendant
            }
        }

        return nil
    }

    private func normalizedFlagName(_ value: String) -> String {
        let filtered = value.lowercased().filter { $0.isLetter || $0.isNumber }
        if filtered.first == "b" {
            return String(filtered.dropFirst())
        }

        return filtered
    }

    private func parseObject(_ data: Data, cursor: inout Int, stringTable: [String]) -> AppInfoNode {
        var node = AppInfoNode()

        while cursor < data.endIndex {
            let type = data[cursor]
            cursor += 1

            if type == 0x08 || type == 0x0B {
                break
            }

            guard cursor + 4 <= data.endIndex else {
                break
            }

            let keyIndex = Int(data.readUInt32(at: cursor))
            cursor += 4
            let key = stringTable.indices.contains(keyIndex) ? stringTable[keyIndex].lowercased() : "\(keyIndex)"

            switch type {
            case 0x00:
                node.children[key] = parseObject(data, cursor: &cursor, stringTable: stringTable)
            case 0x01:
                let (value, next) = data.readNullTerminatedString(at: cursor)
                cursor = next
                node.children[key] = AppInfoNode(value: value)
            case 0x02, 0x0C:
                let value = data.readUInt32(at: cursor)
                cursor += 4
                node.children[key] = AppInfoNode(value: String(value))
            case 0x03, 0x04:
                cursor += 4
            case 0x05:
                let (value, next) = data.readNullTerminatedWideString(at: cursor)
                cursor = next
                node.children[key] = AppInfoNode(value: value)
            case 0x06:
                cursor += 4
            case 0x07, 0x0A:
                cursor += 8
            case 0x0D:
                guard cursor + 4 <= data.endIndex else {
                    cursor = data.endIndex
                    break
                }
                let length = Int(data.readInt32(at: cursor))
                cursor += 4 + max(0, length)
            case 0x14:
                let value = cursor < data.endIndex ? data[cursor] : 0
                cursor += 1
                node.children[key] = AppInfoNode(value: value == 0 ? "0" : "1")
            default:
                cursor = data.endIndex
            }

            if cursor > data.endIndex {
                cursor = data.endIndex
            }
        }

        return node
    }
}

struct SteamAppInfoMetadata {
    var appId: UInt32
    var name: String?
    var type: String?
    var storeCategoryIds: [Int] = []
    var deckCompatibility: SteamDeckCompatibility = .unknown
    var isFamilyShared: Bool = false
}

private struct AppInfoNode {
    var value: String?
    var children: [String: AppInfoNode] = [:]

    var boolValue: Bool? {
        switch value?.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() {
        case "1", "true", "yes":
            return true
        case "0", "false", "no":
            return false
        default:
            return nil
        }
    }

    func path(_ names: String...) -> AppInfoNode? {
        var current: AppInfoNode? = self
        for name in names {
            current = current?.children[name]
        }
        return current
    }

    func firstDescendant(named name: String) -> AppInfoNode? {
        if let direct = children[name] {
            return direct
        }

        for child in children.values {
            if let found = child.firstDescendant(named: name) {
                return found
            }
        }

        return nil
    }
}

private extension Data {
    func readUInt32(at offset: Int) -> UInt32 {
        let bytes = self[offset..<offset + 4]
        return bytes.enumerated().reduce(UInt32(0)) { result, item in
            result | (UInt32(item.element) << UInt32(item.offset * 8))
        }
    }

    func readInt32(at offset: Int) -> Int32 {
        Int32(bitPattern: readUInt32(at: offset))
    }

    func readInt64(at offset: Int) -> Int64 {
        let bytes = self[offset..<offset + 8]
        let value = bytes.enumerated().reduce(UInt64(0)) { result, item in
            result | (UInt64(item.element) << UInt64(item.offset * 8))
        }
        return Int64(bitPattern: value)
    }

    func readNullTerminatedString(at offset: Int) -> (String, Int) {
        var cursor = offset
        var bytes: [UInt8] = []
        while cursor < count {
            let byte = self[cursor]
            cursor += 1
            if byte == 0 {
                break
            }
            bytes.append(byte)
        }
        return (String(bytes: bytes, encoding: .utf8) ?? "", cursor)
    }

    func readNullTerminatedWideString(at offset: Int) -> (String, Int) {
        var cursor = offset
        var values: [UInt16] = []
        while cursor + 1 < count {
            let value = UInt16(self[cursor]) | (UInt16(self[cursor + 1]) << 8)
            cursor += 2
            if value == 0 {
                break
            }
            values.append(value)
        }
        return (String(decoding: values, as: UTF16.self), cursor)
    }
}
