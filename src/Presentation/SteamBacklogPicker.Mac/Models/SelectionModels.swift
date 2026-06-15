import Foundation

struct SelectionFilters: Codable, Equatable {
    var requireInstalled = false
    var excludeDeckUnsupported = false
    var requireMacCompatible = false
    var requiredCollection: String?
    var includedCategories: Set<ProductCategory> = [.game]
    var filterByStorefront = false
    var includedStorefronts: Set<Storefront> = [.steam]

    init() {}

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        requireInstalled = try container.decodeIfPresent(Bool.self, forKey: .requireInstalled) ?? false
        excludeDeckUnsupported = try container.decodeIfPresent(Bool.self, forKey: .excludeDeckUnsupported) ?? false
        requireMacCompatible = try container.decodeIfPresent(Bool.self, forKey: .requireMacCompatible) ?? false
        requiredCollection = try container.decodeIfPresent(String.self, forKey: .requiredCollection)
        includedCategories = try container.decodeIfPresent(Set<ProductCategory>.self, forKey: .includedCategories) ?? [.game]
        filterByStorefront = try container.decodeIfPresent(Bool.self, forKey: .filterByStorefront) ?? false
        includedStorefronts = try container.decodeIfPresent(Set<Storefront>.self, forKey: .includedStorefronts) ?? []
    }
}

struct SelectionPreferences: Codable, Equatable {
    var filters = SelectionFilters()
    var seed: Int?
    var randomPosition = 0
    var recentGameExclusionCount = 0
    var historyLimit = 50

    init() {}

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        filters = try container.decodeIfPresent(SelectionFilters.self, forKey: .filters) ?? SelectionFilters()
        seed = try container.decodeIfPresent(Int.self, forKey: .seed)
        randomPosition = try container.decodeIfPresent(Int.self, forKey: .randomPosition) ?? 0
        recentGameExclusionCount = try container.decodeIfPresent(Int.self, forKey: .recentGameExclusionCount) ?? 0
        historyLimit = try container.decodeIfPresent(Int.self, forKey: .historyLimit) ?? 50
    }
}

struct SelectionHistoryEntry: Codable, Equatable {
    var gameId: String
    var title: String
    var selectedAt: Date
}

extension SelectionFilters {
    func normalized() -> SelectionFilters {
        var copy = self
        copy.requiredCollection = copy.requiredCollection?
            .trimmingCharacters(in: .whitespacesAndNewlines)
        if copy.requiredCollection?.isEmpty == true {
            copy.requiredCollection = nil
        }

        copy.includedCategories = Set(copy.includedCategories.map(Self.normalizedCategory))
        copy.includedStorefronts = Set(copy.includedStorefronts.filter { $0 != .unknown })
        return copy
    }

    private static func normalizedCategory(_ category: ProductCategory) -> ProductCategory {
        switch category {
        case .unknown:
            return .game
        case .dlc:
            return .other
        default:
            return category
        }
    }
}

extension SelectionPreferences {
    func normalized() -> SelectionPreferences {
        var copy = self
        copy.filters = copy.filters.normalized()
        if copy.randomPosition < 0 {
            copy.randomPosition = 0
        }
        copy.recentGameExclusionCount = max(0, copy.recentGameExclusionCount)
        copy.historyLimit = max(0, copy.historyLimit)
        return copy
    }
}

struct DotNetCompatibleRandom {
    private static let maxInt = 2_147_483_647
    private static let seedBase = 161_803_398
    private var seedArray = Array(repeating: 0, count: 56)
    private var inext = 0
    private var inextp = 21

    init(seed: Int) {
        let subtraction = seed == Int.min ? Int.max : abs(seed)
        var mj = Self.seedBase - subtraction
        if mj < 0 {
            mj += Self.maxInt
        }

        seedArray[55] = mj
        var mk = 1

        for i in 1..<55 {
            let ii = (21 * i) % 55
            seedArray[ii] = mk
            mk = mj - mk
            if mk < 0 {
                mk += Self.maxInt
            }
            mj = seedArray[ii]
        }

        for _ in 0..<4 {
            for i in 1..<56 {
                seedArray[i] -= seedArray[1 + ((i + 30) % 55)]
                if seedArray[i] < 0 {
                    seedArray[i] += Self.maxInt
                }
            }
        }
    }

    mutating func nextDouble() -> Double {
        Double(internalSample()) * (1.0 / Double(Self.maxInt))
    }

    private mutating func internalSample() -> Int {
        var next = inext + 1
        if next >= 56 {
            next = 1
        }

        var nextp = inextp + 1
        if nextp >= 56 {
            nextp = 1
        }

        var result = seedArray[next] - seedArray[nextp]
        if result == Self.maxInt {
            result -= 1
        }
        if result < 0 {
            result += Self.maxInt
        }

        seedArray[next] = result
        inext = next
        inextp = nextp
        return result
    }
}
