import Foundation

struct SteamLibraryService {
    private let fileManager: FileManager
    private let steamDirectoryOverride: URL?
    private let appNameResolver: SteamAppNameResolving
    private let parser = VDFParser()

    init(
        fileManager: FileManager = .default,
        steamDirectory: URL? = nil,
        appNameResolver: SteamAppNameResolving = SteamAppNameResolver()
    ) {
        self.fileManager = fileManager
        steamDirectoryOverride = steamDirectory
        self.appNameResolver = appNameResolver
    }

    func loadLibrary() throws -> [GameEntry] {
        guard let steamDirectory = findSteamDirectory() else {
            return []
        }

        let libraries = findLibraryFolders(steamDirectory: steamDirectory)
        let metadata = loadMetadata(steamDirectory: steamDirectory)
        var entries: [UInt32: GameEntry] = [:]

        for library in libraries {
            for entry in loadManifests(in: library, steamDirectory: steamDirectory, metadata: metadata) {
                if let appId = entry.steamAppId {
                    entries[appId] = entry
                }
            }
        }

        for app in metadata.apps.values where entries[app.appId] == nil {
            entries[app.appId] = makeAvailableEntry(
                app: app,
                steamDirectory: steamDirectory,
                libraries: libraries,
                collections: metadata.collections[app.appId] ?? []
            )
        }

        applyDynamicCollections(metadata.collectionDefinitions, entries: &entries)
        resolveMissingNames(entries: &entries)

        return entries.values.sorted(by: compareLibraryEntries)
    }

    private func compareLibraryEntries(_ left: GameEntry, _ right: GameEntry) -> Bool {
        let titleComparison = left.title.localizedCaseInsensitiveCompare(right.title)
        if titleComparison != .orderedSame {
            return titleComparison == .orderedAscending
        }

        let storefrontComparison = storefrontRank(left.storefront) - storefrontRank(right.storefront)
        if storefrontComparison != 0 {
            return storefrontComparison < 0
        }

        let leftStoreId = left.steamAppId.map(String.init) ?? left.title.lowercased()
        let rightStoreId = right.steamAppId.map(String.init) ?? right.title.lowercased()
        let storeIdComparison = leftStoreId.localizedCaseInsensitiveCompare(rightStoreId)
        if storeIdComparison != .orderedSame {
            return storeIdComparison == .orderedAscending
        }

        return (left.steamAppId ?? 0) < (right.steamAppId ?? 0)
    }

    private func storefrontRank(_ storefront: Storefront) -> Int {
        switch storefront {
        case .unknown:
            return 0
        case .steam:
            return 1
        }
    }

    private func findSteamDirectory() -> URL? {
        if let steamDirectoryOverride {
            return steamDirectoryOverride
        }

        let candidates = [
            ProcessInfo.processInfo.environment["STEAM_PATH"].map(URL.init(fileURLWithPath:)),
            fileManager.homeDirectoryForCurrentUser
                .appendingPathComponent("Library")
                .appendingPathComponent("Application Support")
                .appendingPathComponent("Steam"),
            fileManager.homeDirectoryForCurrentUser
                .appendingPathComponent(".steam")
                .appendingPathComponent("steam")
        ].compactMap { $0 }

        return candidates.first { candidate in
            fileManager.fileExists(atPath: candidate.appendingPathComponent("steamapps/libraryfolders.vdf").path)
        }
    }

    private func findLibraryFolders(steamDirectory: URL) -> [URL] {
        var libraries: [URL] = [steamDirectory]
        let libraryFoldersURL = steamDirectory.appendingPathComponent("steamapps/libraryfolders.vdf")

        guard
            let root = try? parser.parseFile(libraryFoldersURL),
            let libraryFolders = root.child("LibraryFolders")
        else {
            return libraries
        }

        collectLibraryPaths(from: libraryFolders).forEach { path in
            let url = URL(fileURLWithPath: path)
            if !libraries.contains(url) {
                libraries.append(url)
            }
        }

        return libraries
    }

    private func collectLibraryPaths(from node: VDFNode) -> [String] {
        var result: [String] = []

        for child in node.allChildren {
            if child.name.caseInsensitiveCompare("path") == .orderedSame || child.name.caseInsensitiveCompare("contentpath") == .orderedSame {
                if let value = child.value, !value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    result.append(value)
                }
                continue
            }

            if UInt(child.name) != nil, let nested = child.child("path")?.value ?? child.child("contentpath")?.value {
                result.append(nested)
                continue
            }

            result.append(contentsOf: collectLibraryPaths(from: child))
        }

        return Array(NSOrderedSet(array: result)) as? [String] ?? result
    }

    private func loadManifests(
        in library: URL,
        steamDirectory: URL,
        metadata: SteamMetadata
    ) -> [GameEntry] {
        let steamApps = library.appendingPathComponent("steamapps")
        guard let enumerator = try? fileManager.contentsOfDirectory(at: steamApps, includingPropertiesForKeys: nil) else {
            return []
        }

        return enumerator.compactMap { manifestURL in
            guard manifestURL.lastPathComponent.hasPrefix("appmanifest_"), manifestURL.pathExtension == "acf" else {
                return nil
            }

            return loadManifest(
                manifestURL,
                steamDirectory: steamDirectory,
                library: library,
                metadata: metadata
            )
        }
    }

    private func loadManifest(
        _ manifestURL: URL,
        steamDirectory: URL,
        library: URL,
        metadata: SteamMetadata
    ) -> GameEntry? {
        guard
            let root = try? parser.parseFile(manifestURL),
            let appState = root.child("AppState"),
            let appIdText = appState.child("appid")?.value,
            let appId = UInt32(appIdText)
        else {
            return nil
        }

        let appMetadata = mergedAppMetadata(appId: appId, metadata: metadata)
        let title = appMetadata?.name
            ?? appState.child("name")?.value
            ?? appState.path("UserConfig", "name")?.value
            ?? "App \(appId)"
        let sizeOnDisk = appState.child("SizeOnDisk")?.value.flatMap(Int64.init)
        let lastPlayed = appState.path("UserConfig", "LastPlayed")?.value
            .flatMap(Int64.init)
            .flatMap { $0 > 0 ? Date(timeIntervalSince1970: TimeInterval($0)) : nil }
        let ownership = appMetadata?.ownershipType ?? .unknown
        let stateFlags = appState.child("StateFlags")?.value.flatMap(UInt32.init)
        let installState: InstallState = stateFlags.map { ($0 & 4) != 0 ? .installed : .available } ?? .unknown
        let tags = Array(metadata.collections[appId] ?? []).sorted { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending }
        let artworkURLs = findHeroImages(appId: appId, steamDirectory: steamDirectory, library: library)

        return GameEntry(
            storefront: .steam,
            steamAppId: appId,
            title: title,
            ownershipType: ownership,
            installState: installState,
            productCategory: appMetadata?.category ?? .game,
            sizeOnDisk: sizeOnDisk,
            lastPlayed: lastPlayed,
            tags: tags,
            storeCategoryIds: appMetadata?.storeCategoryIds ?? [],
            deckCompatibility: appMetadata?.deckCompatibility ?? .unknown,
            supportedPlatforms: appMetadata?.supportedPlatforms ?? [],
            coverURL: artworkURLs.first,
            coverURLs: artworkURLs
        )
    }

    private func makeAvailableEntry(
        app: SteamAppMetadata,
        steamDirectory: URL,
        libraries: [URL],
        collections: Set<String>
    ) -> GameEntry {
        let tags = Array(collections).sorted { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending }
        let coverLibrary = libraries.first ?? steamDirectory
        let artworkURLs = findHeroImages(appId: app.appId, steamDirectory: steamDirectory, library: coverLibrary)

        return GameEntry(
            storefront: .steam,
            steamAppId: app.appId,
            title: app.name ?? "App \(app.appId)",
            ownershipType: app.ownershipType,
            installState: .available,
            productCategory: app.category,
            sizeOnDisk: nil,
            lastPlayed: nil,
            tags: tags,
            storeCategoryIds: app.storeCategoryIds,
            deckCompatibility: app.deckCompatibility,
            supportedPlatforms: app.supportedPlatforms,
            coverURL: artworkURLs.first,
            coverURLs: artworkURLs
        )
    }

    private func mergedAppMetadata(appId: UInt32, metadata: SteamMetadata) -> SteamAppMetadata? {
        var app = metadata.apps[appId]
        guard let info = metadata.appInfo[appId] else {
            return app
        }

        if app == nil {
            app = SteamAppMetadata(appId: appId, name: nil, type: nil, isFamilyShared: false)
        }
        if info.name?.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty == false {
            app?.name = info.name
        }
        if info.type?.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty == false {
            app?.type = info.type
        }
        if !info.storeCategoryIds.isEmpty {
            app?.storeCategoryIds = info.storeCategoryIds
        }
        if info.deckCompatibility != .unknown {
            app?.deckCompatibility = info.deckCompatibility
        }
        if !info.supportedPlatforms.isEmpty {
            app?.supportedPlatforms = info.supportedPlatforms
        }
        return app
    }

    private func findHeroImages(appId: UInt32, steamDirectory: URL, library: URL) -> [URL] {
        let candidateFiles = [
            "\(appId)_header.jpg",
            "\(appId)_capsule_616x353.jpg",
            "\(appId)_library_hero.jpg",
            "\(appId)_library_600x900.jpg",
            "\(appId)/header.jpg",
            "\(appId)/library_hero.jpg",
            "\(appId)/library_600x900.jpg",
            "\(appId)/capsule_616x353.jpg"
        ]

        var urls: [URL] = []
        var seen = Set<URL>()

        for root in [steamDirectory, library] {
            for candidate in candidateFiles {
                let url = root.appendingPathComponent("appcache/librarycache").appendingPathComponent(candidate)
                if fileManager.fileExists(atPath: url.path), seen.insert(url).inserted {
                    urls.append(url)
                }
            }
        }

        let remoteCandidates = [
            "https://cdn.cloudflare.steamstatic.com/steam/apps/\(appId)/header.jpg",
            "https://cdn.cloudflare.steamstatic.com/steam/apps/\(appId)/capsule_616x353.jpg",
            "https://steamdb.info/static/cdn/steam/apps/\(appId)/header.jpg",
            "https://cdn.cloudflare.steamstatic.com/steam/apps/\(appId)/library_600x900.jpg"
        ]
        for value in remoteCandidates {
            if let url = URL(string: value), seen.insert(url).inserted {
                urls.append(url)
            }
        }

        return urls
    }

    private func applyDynamicCollections(
        _ definitions: [SteamCollectionDefinition],
        entries: inout [UInt32: GameEntry]
    ) {
        guard !definitions.isEmpty, !entries.isEmpty else {
            return
        }

        for definition in definitions {
            for appId in definition.explicitAppIds {
                addCollection(definition.name, to: appId, entries: &entries)
            }

            guard let filterSpec = definition.filterSpec else {
                continue
            }

            for (appId, entry) in entries where !definition.explicitAppIds.contains(appId) {
                if matchesCollection(entry: entry, filterSpec: filterSpec) {
                    addCollection(definition.name, to: appId, entries: &entries)
                }
            }
        }
    }

    private func addCollection(_ name: String, to appId: UInt32, entries: inout [UInt32: GameEntry]) {
        guard var entry = entries[appId], !name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            return
        }

        if !entry.tags.contains(where: { $0.caseInsensitiveCompare(name) == .orderedSame }) {
            entry.tags.append(name)
            entry.tags.sort { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending }
            entries[appId] = entry
        }
    }

    private func matchesCollection(entry: GameEntry, filterSpec: SteamCollectionFilterSpec) -> Bool {
        for group in filterSpec.groups {
            if group.options.isEmpty {
                continue
            }

            let groupMatch = group.acceptUnion
                ? group.options.contains { matchesFilterOption(entry: entry, option: $0) }
                : group.options.allSatisfy { matchesFilterOption(entry: entry, option: $0) }

            if !groupMatch {
                return false
            }
        }

        return true
    }

    private func matchesFilterOption(entry: GameEntry, option: Int) -> Bool {
        switch option {
        case 1:
            return entry.installState == .installed
        case 3:
            return supportsVr(entry.storeCategoryIds)
        case 7:
            return entry.storeCategoryIds.contains(2)
        case 8:
            return entry.storeCategoryIds.contains { [1, 9, 38, 48, 49].contains($0) }
        case 13:
            return entry.deckCompatibility == .verified || entry.deckCompatibility == .playable
        default:
            return false
        }
    }

    private func supportsVr(_ categoryIds: [Int]) -> Bool {
        categoryIds.contains { categoryId in
            switch categoryId {
            case 31, 52, 53, 54:
                return true
            default:
                return false
            }
        }
    }

    private func resolveMissingNames(entries: inout [UInt32: GameEntry]) {
        let missingIds = Set(entries.compactMap { appId, entry in
            entry.title == "App \(appId)" ? appId : nil
        })
        guard !missingIds.isEmpty else {
            return
        }

        let resolvedNames = appNameResolver.resolveNames(for: missingIds)
        for (appId, name) in resolvedNames {
            guard var entry = entries[appId], entry.title == "App \(appId)" else {
                continue
            }
            entry.title = name
            entries[appId] = entry
        }
    }
}

private struct SteamMetadata {
    var currentUserId: String?
    var apps: [UInt32: SteamAppMetadata] = [:]
    var appInfo: [UInt32: SteamAppInfoMetadata] = [:]
    var collections: [UInt32: Set<String>] = [:]
    var collectionDefinitions: [SteamCollectionDefinition] = []
}

private struct SteamAppMetadata {
    var appId: UInt32
    var name: String?
    var type: String?
    var isFamilyShared: Bool
    var ownershipType: OwnershipType = .unknown
    var storeCategoryIds: [Int] = []
    var deckCompatibility: SteamDeckCompatibility = .unknown
    var supportedPlatforms: Set<SteamPlatform> = []

    var category: ProductCategory {
        ProductCategory.fromSteamType(type)
    }
}

private struct SteamCollectionDefinition {
    var id: String
    var name: String
    var explicitAppIds: Set<UInt32>
    var filterSpec: SteamCollectionFilterSpec?
    var timestamp: Int64
}

private struct SteamCollectionFilterSpec {
    var groups: [SteamCollectionFilterGroup]
}

private struct SteamCollectionFilterGroup {
    var options: [Int]
    var acceptUnion: Bool
}

private extension SteamLibraryService {
    func loadMetadata(steamDirectory: URL) -> SteamMetadata {
        guard
            let loginUsers = parseFile(steamDirectory.appendingPathComponent("config/loginusers.vdf")),
            let users = loginUsers.child("users"),
            let steamId = findMostRecentUser(in: users)
        else {
            return SteamMetadata()
        }

        var metadata = SteamMetadata(currentUserId: steamId)
        for candidate in userDirectoryCandidates(steamId: steamId) {
            mergeLocalConfig(steamDirectory: steamDirectory, candidate: candidate, metadata: &metadata)
            mergeSharedConfig(steamDirectory: steamDirectory, candidate: candidate, metadata: &metadata)
            mergeLibraryCache(steamDirectory: steamDirectory, candidate: candidate, metadata: &metadata)
            mergeCloudCollections(steamDirectory: steamDirectory, candidate: candidate, metadata: &metadata)
        }
        mergeAppInfo(steamDirectory: steamDirectory, metadata: &metadata)
        return metadata
    }

    func parseFile(_ url: URL) -> VDFNode? {
        return try? parser.parseFile(url)
    }

    func findMostRecentUser(in users: VDFNode) -> String? {
        let explicit = users.allChildren.first { user in
            UInt64(user.name) != nil && user.child("MostRecent")?.value == "1"
        }
        if let explicit {
            return explicit.name
        }

        return users.allChildren.filter { UInt64($0.name) != nil }.max { left, right in
            let leftTimestamp = Int(left.child("Timestamp")?.value ?? "") ?? 0
            let rightTimestamp = Int(right.child("Timestamp")?.value ?? "") ?? 0
            return leftTimestamp < rightTimestamp
        }?.name
    }

    func userDirectoryCandidates(steamId: String) -> [String] {
        var candidates = [steamId]
        if let value = UInt64(steamId) {
            candidates.append(String(value & 0xFFFFFFFF))
        }
        return Array(NSOrderedSet(array: candidates)) as? [String] ?? candidates
    }

    func mergeLocalConfig(steamDirectory: URL, candidate: String, metadata: inout SteamMetadata) {
        guard
            let root = parseFile(steamDirectory.appendingPathComponent("userdata/\(candidate)/config/localconfig.vdf")),
            let store = root.child("UserLocalConfigStore"),
            let apps = findLargestNumericChildrenNode(named: "apps", in: store)
        else {
            return
        }

        for appNode in apps.allChildren {
            guard let appId = UInt32(appNode.name) else { continue }
            var app = metadata.apps[appId] ?? SteamAppMetadata(appId: appId, name: nil, type: nil, isFamilyShared: false)
            app.name = appNode.child("name")?.value ?? app.name
            app.type = appNode.child("AppType")?.value ?? appNode.child("type")?.value ?? app.type
            app.isFamilyShared = appNode.child("IsSubscribedFromFamilySharing")?.value.flatMap(parseBool) ?? false
            let owned = ["is_owned", "owned", "IsSubscribed"].contains { key in
                appNode.child(key)?.value.flatMap(parseBool) == true
            }
            app.ownershipType = app.isFamilyShared ? .familyShared : (owned ? .owned : .unknown)
            metadata.apps[appId] = app
        }
    }

    func mergeSharedConfig(steamDirectory: URL, candidate: String, metadata: inout SteamMetadata) {
        guard
            let root = parseFile(steamDirectory.appendingPathComponent("userdata/\(candidate)/7/remote/sharedconfig.vdf")),
            let store = root.child("UserRoamingConfigStore")
        else {
            return
        }

        let tagLookup = buildTagLookup(store: store)
        if let apps = store.child("apps") {
            for appNode in apps.allChildren {
                guard let appId = UInt32(appNode.name) else { continue }
                upsertApp(appId, metadata: &metadata)
                guard let tags = appNode.child("tags") else { continue }
                for tagNode in tags.allChildren {
                    let name = tagLookup[tagNode.name] ?? tagNode.value ?? tagNode.child("tag")?.value ?? tagNode.name
                    metadata.collections[appId, default: []].insert(name)
                }
            }
        }

        if let collections = store.child("collections") {
            for collection in collections.allChildren {
                guard let name = collection.child("display_name")?.value
                    ?? collection.child("name")?.value
                    ?? collection.child("custom_name")?.value
                    ?? collection.child("localized_name")?.value
                    ?? collection.value,
                    !name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
                else {
                    continue
                }

                for appId in collectAppIds(from: collection) {
                    upsertApp(appId, metadata: &metadata)
                    metadata.collections[appId, default: []].insert(name)
                }
            }
        }
    }

    func mergeLibraryCache(steamDirectory: URL, candidate: String, metadata: inout SteamMetadata) {
        let cacheDirectory = steamDirectory.appendingPathComponent("userdata/\(candidate)/config/librarycache")
        guard let files = try? fileManager.contentsOfDirectory(at: cacheDirectory, includingPropertiesForKeys: nil) else {
            return
        }

        for file in files where file.pathExtension == "json" {
            guard let appId = UInt32(file.deletingPathExtension().lastPathComponent) else {
                continue
            }

            var app = metadata.apps[appId] ?? SteamAppMetadata(appId: appId, name: nil, type: nil, isFamilyShared: false)
            if let data = try? Data(contentsOf: file),
               let json = try? JSONSerialization.jsonObject(with: data) {
                app.name = findString(in: json, keys: ["name", "strName", "app_name", "display_name"]) ?? app.name
                app.type = findString(in: json, keys: ["app_type", "AppType", "type"]) ?? app.type
                let family = findBool(in: json, keys: ["IsSubscribedFromFamilySharing"])
                let owned = findBool(in: json, keys: ["is_owned", "owned", "IsSubscribed"])
                if family == true { app.ownershipType = .familyShared }
                else if owned == true, app.ownershipType == .unknown { app.ownershipType = .owned }
            }
            metadata.apps[appId] = app
        }
    }

    func mergeAppInfo(steamDirectory: URL, metadata: inout SteamMetadata) {
        let appInfoURL = steamDirectory.appendingPathComponent("appcache/appinfo.vdf")
        let appInfo = SteamAppInfoParser().parseAppMetadata(from: appInfoURL)
        metadata.appInfo = appInfo

        for appId in metadata.apps.keys {
            guard let info = appInfo[appId], var app = metadata.apps[appId] else {
                continue
            }

            if info.name?.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty == false {
                app.name = info.name
            }
            if info.type?.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty == false {
                app.type = info.type
            }
            if !info.storeCategoryIds.isEmpty {
                app.storeCategoryIds = info.storeCategoryIds
            }
            if info.deckCompatibility != .unknown {
                app.deckCompatibility = info.deckCompatibility
            }
            if !info.supportedPlatforms.isEmpty {
                app.supportedPlatforms = info.supportedPlatforms
            }
            metadata.apps[appId] = app
        }
    }

    func mergeCloudCollections(steamDirectory: URL, candidate: String, metadata: inout SteamMetadata) {
        let url = steamDirectory
            .appendingPathComponent("userdata/\(candidate)/config/cloudstorage/cloud-storage-namespace-1.json")

        guard
            let data = try? Data(contentsOf: url),
            let root = try? JSONSerialization.jsonObject(with: data) as? [Any]
        else {
            return
        }

        var byId = Dictionary(uniqueKeysWithValues: metadata.collectionDefinitions.map { ($0.id.lowercased(), $0) })

        for item in root {
            guard
                let pair = item as? [Any],
                pair.count == 2,
                let key = pair[0] as? String,
                key.lowercased().hasPrefix("user-collections"),
                let payload = pair[1] as? [String: Any],
                let rawValue = payload["value"] as? String,
                let valueData = rawValue.data(using: .utf8),
                let collection = try? JSONSerialization.jsonObject(with: valueData) as? [String: Any],
                let id = collection["id"] as? String,
                !id.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            else {
                continue
            }

            let timestamp = payload["timestamp"] as? Int64
                ?? (payload["timestamp"] as? NSNumber)?.int64Value
                ?? 0
            let name = (collection["name"] as? String)?.trimmingCharacters(in: .whitespacesAndNewlines)
            let resolvedName = name?.isEmpty == false ? name! : id
            let explicitAppIds = parseAppIdSet(collection["added"])
            let filterSpec = parseFilterSpec(collection["filterSpec"])
            let definition = SteamCollectionDefinition(
                id: id,
                name: resolvedName,
                explicitAppIds: explicitAppIds,
                filterSpec: filterSpec,
                timestamp: timestamp
            )

            let collectionKey = id.lowercased()
            if let existing = byId[collectionKey], existing.timestamp >= timestamp {
                continue
            }

            byId[collectionKey] = definition
        }

        metadata.collectionDefinitions = byId.values.sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
    }

    func upsertApp(_ appId: UInt32, metadata: inout SteamMetadata) {
        if metadata.apps[appId] == nil {
            metadata.apps[appId] = SteamAppMetadata(appId: appId, name: nil, type: nil, isFamilyShared: false)
        }
    }

    func buildTagLookup(store: VDFNode) -> [String: String] {
        guard let tags = store.child("tags") else { return [:] }
        var lookup: [String: String] = [:]
        for tagNode in tags.allChildren {
            if let name = tagNode.value ?? tagNode.child("tag")?.value, !name.isEmpty {
                lookup[tagNode.name] = name
            }
        }
        return lookup
    }

    func findLargestNumericChildrenNode(named name: String, in node: VDFNode) -> VDFNode? {
        var best: VDFNode?
        var bestCount = -1

        func visit(_ current: VDFNode) {
            if current.name.caseInsensitiveCompare(name) == .orderedSame {
                let count = current.allChildren.filter { UInt32($0.name) != nil }.count
                if count > bestCount {
                    best = current
                    bestCount = count
                }
            }

            for child in current.allChildren {
                visit(child)
            }
        }

        visit(node)
        return best
    }

    func collectAppIds(from node: VDFNode, inMembershipContext: Bool = false) -> Set<UInt32> {
        let membershipNames: Set<String> = [
            "apps", "appids", "app_ids", "added", "add", "members", "children",
            "included", "includedapps", "app_list", "applist", "appidslist", "appidlist"
        ]
        let currentIsMembership = inMembershipContext || membershipNames.contains(node.name.lowercased())
        var result = Set<UInt32>()

        if currentIsMembership, let value = node.value, let appId = UInt32(value) {
            result.insert(appId)
        }

        for child in node.allChildren {
            if currentIsMembership,
               child.value?.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty != false,
               let appId = UInt32(child.name) {
                result.insert(appId)
            }
            result.formUnion(collectAppIds(from: child, inMembershipContext: currentIsMembership))
        }

        return result
    }

    func parseAppIdSet(_ value: Any?) -> Set<UInt32> {
        guard let array = value as? [Any] else {
            return []
        }

        var result = Set<UInt32>()
        for item in array {
            if let number = jsonNumber(item) {
                result.insert(number.uint32Value)
            }
        }
        return result
    }

    func parseFilterSpec(_ value: Any?) -> SteamCollectionFilterSpec? {
        guard
            let dictionary = value as? [String: Any],
            let groups = dictionary["filterGroups"] as? [Any]
        else {
            return nil
        }

        let parsedGroups = groups.compactMap { item -> SteamCollectionFilterGroup? in
            guard let group = item as? [String: Any] else {
                return nil
            }

            let options = (group["rgOptions"] as? [Any] ?? []).compactMap { option -> Int? in
                jsonNumber(option)?.intValue
            }

            guard !options.isEmpty else {
                return nil
            }

            let acceptUnion = (group["bAcceptUnion"] as? Bool)
                ?? false
            return SteamCollectionFilterGroup(options: options, acceptUnion: acceptUnion)
        }

        return parsedGroups.isEmpty ? nil : SteamCollectionFilterSpec(groups: parsedGroups)
    }

    func jsonNumber(_ value: Any) -> NSNumber? {
        guard let number = value as? NSNumber, CFGetTypeID(number) != CFBooleanGetTypeID() else {
            return nil
        }
        return number
    }

    func parseBool(_ value: String) -> Bool? {
        switch value.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() {
        case "1", "true", "yes":
            return true
        case "0", "false", "no":
            return false
        default:
            return nil
        }
    }

    func findString(in json: Any, keys: Set<String>) -> String? {
        if let dictionary = json as? [String: Any] {
            for key in keys {
                if let value = dictionary[key] as? String, !value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    return value
                }
            }
            for value in dictionary.values {
                if let found = findString(in: value, keys: keys) {
                    return found
                }
            }
        } else if let array = json as? [Any] {
            for value in array {
                if let found = findString(in: value, keys: keys) {
                    return found
                }
            }
        }
        return nil
    }

    func findBool(in json: Any, keys: Set<String>) -> Bool? {
        if let dictionary = json as? [String: Any] {
            for key in keys {
                if let value = dictionary[key] as? Bool {
                    return value
                }
                if let value = dictionary[key] as? String, let parsed = parseBool(value) {
                    return parsed
                }
                if let value = dictionary[key] as? NSNumber {
                    return value.boolValue
                }
            }
            for value in dictionary.values {
                if let found = findBool(in: value, keys: keys) {
                    return found
                }
            }
        } else if let array = json as? [Any] {
            for value in array {
                if let found = findBool(in: value, keys: keys) {
                    return found
                }
            }
        }
        return nil
    }
}
