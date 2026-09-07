import Foundation
import libzstd

struct SteamAppInfoParser {
    private static let maximumFileBytes = 512 * 1024 * 1024
    private static let maximumPayloadBytes = 16 * 1024 * 1024

    func parseAppMetadata(from url: URL) -> [UInt32: SteamAppInfoMetadata] {
        guard url.isFileURL, let file = try? FileHandle(forReadingFrom: url) else { return [:] }
        defer { try? file.close() }
        guard let length = try? file.seekToEnd(), length <= UInt64(Self.maximumFileBytes) else { return [:] }
        do { try file.seek(toOffset: 0) } catch { return [:] }
        guard let data = try? file.read(upToCount: Int(length) + 1),
              data.count <= Self.maximumFileBytes else { return [:] }
        do { return try parse(data) } catch { return [:] }
    }

    private func parse(_ data: Data) throws -> [UInt32: SteamAppInfoMetadata] {
        var reader = AppInfoReader(data: data)
        let rawMagic = try reader.uint32()
        let version = UInt8(rawMagic & 0xFF)
        guard rawMagic >> 8 == 0x075644, (39...42).contains(version) else { throw AppInfoError.malformed }
        _ = try reader.uint32()
        var stringTable: [String] = []
        var entriesEnd = data.count
        if version >= 41 {
            let tableOffset = try reader.uint64()
            guard tableOffset >= UInt64(reader.position), tableOffset <= UInt64(data.count - 4) else {
                throw AppInfoError.malformed
            }
            entriesEnd = Int(tableOffset)
            var tableReader = AppInfoReader(data: data, position: entriesEnd)
            let count = Int(try tableReader.uint32())
            guard count <= 1_000_000, count <= tableReader.remaining else { throw AppInfoError.limitExceeded }
            for _ in 0..<count { stringTable.append(try tableReader.string()) }
        }
        var result: [UInt32: SteamAppInfoMetadata] = [:]
        var decodedBytes = 0
        while reader.position < entriesEnd {
            guard entriesEnd - reader.position >= 4 else { break }
            let appId = try reader.uint32()
            // Steam's footer is just AppID=0, without a following entry size.
            if appId == 0 { break }
            guard entriesEnd - reader.position >= 4 else { break }
            let size = Int(try reader.uint32())
            guard size <= entriesEnd - reader.position else { break }
            let end = reader.position + size
            let metadataLength = version >= 40 ? 60 : 40
            defer { reader.position = end }
            guard size >= metadataLength, size - metadataLength <= Self.maximumPayloadBytes else { continue }
            let payload = Data(data[(reader.position + metadataLength)..<end])
            do {
                let decoded = try decodePayload(payload)
                guard decoded.count <= 256 * 1024 * 1024 - decodedBytes else { break }
                decodedBytes += decoded.count
                var payloadReader = AppInfoReader(data: decoded)
                var nodes = 0
                let root = try parseObject(&payloadReader, stringTable: version >= 41 ? stringTable : nil, depth: 0, nodes: &nodes)
                if let parsed = metadata(from: root, appId: appId) { result[appId] = parsed }
            } catch {
                // Reject only this corrupt entry; retain other valid cached metadata.
            }
        }
        return result
    }

    private func decodePayload(_ payload: Data) throws -> Data {
        var header = AppInfoReader(data: payload)
        guard payload.count >= 4 else { throw AppInfoError.malformed }
        guard try header.uint32() == 0x615A5356 else { return payload }
        guard payload.count >= 23 else { throw AppInfoError.malformed }
        let checksum = try header.uint32()
        var footer = AppInfoReader(data: payload, position: payload.count - 15)
        guard try footer.uint32() == checksum else { throw AppInfoError.malformed }
        let expectedSize = Int(try footer.uint32())
        guard expectedSize > 0, expectedSize <= Self.maximumPayloadBytes,
              payload.suffix(3) == Data([0x7a, 0x73, 0x76]) else { throw AppInfoError.limitExceeded }
        let compressed = Data(payload[8..<(payload.count - 15)])
        var decoded = Data(count: expectedSize)
        let written = decoded.withUnsafeMutableBytes { output in
            compressed.withUnsafeBytes { input in
                ZSTD_decompress(output.baseAddress, output.count, input.baseAddress, input.count)
            }
        }
        guard ZSTD_isError(written) == 0, written == expectedSize,
              Self.crc32(decoded) == checksum else { throw AppInfoError.malformed }
        return decoded
    }

    private static let crcTable: [UInt32] = (0..<256).map { index in
        var value = UInt32(index)
        for _ in 0..<8 { value = (value >> 1) ^ ((value & 1) == 0 ? 0 : 0xEDB88320) }
        return value
    }

    static func crc32(_ data: Data) -> UInt32 {
        var crc: UInt32 = 0xFFFFFFFF
        for byte in data { crc = (crc >> 8) ^ crcTable[Int((crc ^ UInt32(byte)) & 0xFF)] }
        return ~crc
    }

    private func metadata(from root: AppInfoNode, appId: UInt32) -> SteamAppInfoMetadata? {
        let common = root.path("appinfo", "common") ?? root.children["common"] ?? root.firstDescendant(named: "common")
        let name = common?.children["name"]?.value
        let type = common?.children["type"]?.value
        let categories = extractCategoryIds(from: common)
        let deck = extractDeckCompatibility(from: common)
        let platforms = extractSupportedPlatforms(from: common)
        let family = extractFamilySharingFlag(from: root) ?? false
        guard name != nil || type != nil || !categories.isEmpty || deck != .unknown || !platforms.isEmpty || family else { return nil }
        return SteamAppInfoMetadata(appId: appId, name: name, type: type, storeCategoryIds: categories,
                                    deckCompatibility: deck, supportedPlatforms: platforms, isFamilyShared: family)
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
            return .unsupported
        case 2:
            return .playable
        case 3:
            return .verified
        default:
            return .unknown
        }
    }

    private func extractSupportedPlatforms(from common: AppInfoNode?) -> Set<SteamPlatform> {
        guard let value = common?.children["oslist"]?.value else {
            return []
        }

        var platforms = Set<SteamPlatform>()
        for rawItem in value.split(separator: ",") {
            switch rawItem.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() {
            case "windows", "win":
                platforms.insert(.windows)
            case "macos", "mac", "osx":
                platforms.insert(.macOS)
            case "linux", "steamdeck":
                platforms.insert(.linux)
            default:
                continue
            }
        }

        return platforms
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

    private func parseObject(_ reader: inout AppInfoReader, stringTable: [String]?, depth: Int, nodes: inout Int) throws -> AppInfoNode {
        guard depth < 64 else { throw AppInfoError.limitExceeded }
        var node = AppInfoNode()
        while reader.remaining > 0 {
            let type = try reader.byte()
            if type == 0x08 || type == 0x0B { return node }
            nodes += 1
            guard nodes <= 100_000 else { throw AppInfoError.limitExceeded }
            let key: String
            if let stringTable {
                let index = Int(try reader.uint32())
                guard stringTable.indices.contains(index) else { throw AppInfoError.malformed }
                key = stringTable[index].lowercased()
            } else {
                key = try reader.string().lowercased()
            }
            switch type {
            case 0x00:
                node.children[key] = try parseObject(&reader, stringTable: stringTable, depth: depth + 1, nodes: &nodes)
            case 0x01:
                node.children[key] = AppInfoNode(value: try reader.string())
            case 0x02, 0x0C:
                node.children[key] = AppInfoNode(value: String(try reader.uint32()))
            case 0x03, 0x04, 0x06:
                try reader.skip(4)
            case 0x05:
                node.children[key] = AppInfoNode(value: try reader.wideString())
            case 0x07, 0x0A:
                try reader.skip(8)
            case 0x0D:
                let length = Int(try reader.uint32())
                try reader.skip(length)
            case 0x14:
                node.children[key] = AppInfoNode(value: try reader.byte() == 0 ? "0" : "1")
            default:
                throw AppInfoError.malformed
            }
        }
        guard depth == 0 else { throw AppInfoError.malformed }
        return node
    }
}

struct SteamAppInfoMetadata {
    var appId: UInt32
    var name: String?
    var type: String?
    var storeCategoryIds: [Int] = []
    var deckCompatibility: SteamDeckCompatibility = .unknown
    var supportedPlatforms: Set<SteamPlatform> = []
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

private enum AppInfoError: Error { case malformed, limitExceeded }

private struct AppInfoReader {
    let data: Data
    var position = 0
    var remaining: Int { data.count - position }

    mutating func skip(_ count: Int) throws {
        guard count >= 0, count <= remaining else { throw AppInfoError.malformed }
        position += count
    }

    mutating func byte() throws -> UInt8 {
        guard remaining >= 1 else { throw AppInfoError.malformed }
        defer { position += 1 }
        return data[position]
    }

    mutating func uint32() throws -> UInt32 {
        guard remaining >= 4 else { throw AppInfoError.malformed }
        var value: UInt32 = 0
        for shift in 0..<4 { value |= UInt32(try byte()) << (shift * 8) }
        return value
    }

    mutating func uint64() throws -> UInt64 {
        let low = UInt64(try uint32())
        return low | (UInt64(try uint32()) << 32)
    }

    mutating func string() throws -> String {
        let start = position
        while remaining > 0 {
            if try byte() == 0 {
                // Cached localized values can contain legacy/non-UTF8 bytes. A bad display
                // string must not discard every other field in an otherwise bounded entry.
                return String(decoding: data[start..<(position - 1)], as: UTF8.self)
            }
            guard position - start <= 1024 * 1024 else { throw AppInfoError.limitExceeded }
        }
        throw AppInfoError.malformed
    }

    mutating func wideString() throws -> String {
        var values: [UInt16] = []
        while remaining >= 2 {
            let low = UInt16(try byte())
            let value = low | (UInt16(try byte()) << 8)
            if value == 0 { return String(decoding: values, as: UTF16.self) }
            guard values.count < 512 * 1024 else { throw AppInfoError.limitExceeded }
            values.append(value)
        }
        throw AppInfoError.malformed
    }
}
