import Foundation

enum Storefront: String, Codable, CaseIterable {
    case unknown
    case steam
}

enum OwnershipType: String, Codable {
    case unknown
    case owned
    case familyShared
}

enum InstallState: String, Codable {
    case unknown
    case installed
    case available
    case shared
}

enum ProductCategory: String, Codable, CaseIterable, Identifiable {
    case unknown
    case game
    case soundtrack
    case software
    case tool
    case video
    case dlc
    case other

    var id: String { rawValue }
}

enum SteamDeckCompatibility: String, Codable {
    case unknown
    case verified
    case playable
    case unsupported
}

struct GameEntry: Codable, Identifiable, Equatable {
    let storefront: Storefront
    let steamAppId: UInt32?
    var title: String
    var ownershipType: OwnershipType
    var installState: InstallState
    var productCategory: ProductCategory
    var sizeOnDisk: Int64?
    var lastPlayed: Date?
    var tags: [String]
    var storeCategoryIds: [Int] = []
    var deckCompatibility: SteamDeckCompatibility
    var coverURL: URL?
    var coverURLs: [URL] = []

    var id: String {
        switch storefront {
        case .steam:
            return "steam:\(steamAppId.map(String.init) ?? "unknown")"
        case .unknown:
            return "unknown:\(title.lowercased())"
        }
    }

    static var empty: GameEntry {
        GameEntry(
            storefront: .unknown,
            steamAppId: nil,
            title: "",
            ownershipType: .unknown,
            installState: .unknown,
            productCategory: .game,
            sizeOnDisk: nil,
            lastPlayed: nil,
            tags: [],
            storeCategoryIds: [],
            deckCompatibility: .unknown,
            coverURL: nil,
            coverURLs: []
        )
    }

    var artworkURLs: [URL] {
        if !coverURLs.isEmpty {
            return coverURLs
        }

        return coverURL.map { [$0] } ?? []
    }
}

extension ProductCategory {
    static func fromSteamType(_ value: String?) -> ProductCategory {
        switch value?.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() {
        case "game":
            return .game
        case "music", "audio", "soundtrack":
            return .soundtrack
        case "application", "software":
            return .software
        case "tool":
            return .tool
        case "video", "movie", "series", "tv", "episode":
            return .video
        case "dlc", "demo", "mod", "advertising", "hardware", "plugin", "config", "beta":
            return .other
        case .some:
            return .other
        case .none:
            return .game
        }
    }
}
