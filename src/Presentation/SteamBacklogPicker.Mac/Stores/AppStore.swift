import AppKit
import Foundation
import SwiftUI

@MainActor
final class AppStore: ObservableObject {
    @Published var library: [GameEntry] = []
    @Published var selectedGame: GameEntry?
    @Published var statusMessage = ""
    @Published var isRefreshing = false
    @Published var isDrawing = false
    @Published var preferences: SelectionPreferences {
        didSet {
            var normalized = preferences.normalized()
            if oldValue.seed != normalized.seed {
                normalized.randomPosition = 0
            }
            if normalized != preferences {
                preferences = normalized
                return
            }
            if oldValue.seed != preferences.seed || oldValue.randomPosition != preferences.randomPosition {
                refreshSeededRandomFromPreferences()
            }
            trimHistory()
            savePreferences()
            updateEligibilitySummary()
        }
    }
    @Published var language: AppLanguage {
        didSet {
            if persistState {
                userDefaults.set(language.rawValue, forKey: Self.languageKey)
            }
            applyStatus()
            languageDidChange?()
        }
    }

    private static let preferencesKey = "SteamBacklogPicker.SelectionPreferences"
    private static let historyKey = "SteamBacklogPicker.SelectionHistory"
    private static let languageKey = "SteamBacklogPicker.Language"

    private enum StatusState {
        case loadingLibrary
        case drawing
        case drawn(String)
        case noGamesFound
        case noMatches(total: Int)
        case allEligible(total: Int)
        case filtered(eligible: Int, total: Int)
        case raw(String)
    }

    private let loadLibraryAction: @Sendable () throws -> [GameEntry]
    private let openURL: (URL) -> Bool
    private let notificationSender: GameNotificationSending
    private let updateChecker: AppUpdateChecking
    private let diagnosticLogger: DiagnosticLogging
    private let randomDouble: () -> Double
    private let drawDelayNanoseconds: UInt64
    private let persistState: Bool
    private let userDefaults: UserDefaults
    private var statusState: StatusState = .loadingLibrary
    private var history: [SelectionHistoryEntry]
    private var seededRandom: DotNetCompatibleRandom?
    private var seededRandomSeed: Int?
    private var seededRandomPosition = 0
    var languageDidChange: (() -> Void)?

    init(
        loadLibrary: @escaping @Sendable () throws -> [GameEntry] = { try SteamLibraryService().loadLibrary() },
        openURL: @escaping (URL) -> Bool = { NSWorkspace.shared.open($0) },
        notificationSender: GameNotificationSending = MacGameNotificationService(),
        updateChecker: AppUpdateChecking = NoOpMacAppUpdateService(),
        diagnosticLogger: DiagnosticLogging = MacDiagnosticLogger(),
        randomDouble: @escaping () -> Double = { Double.random(in: 0..<1) },
        drawDelayNanoseconds: UInt64 = 850_000_000,
        initialPreferences: SelectionPreferences? = nil,
        initialHistory: [SelectionHistoryEntry]? = nil,
        initialLanguage: AppLanguage? = nil,
        persistState: Bool = true,
        userDefaults: UserDefaults = .standard
    ) {
        loadLibraryAction = loadLibrary
        self.openURL = openURL
        self.notificationSender = notificationSender
        self.updateChecker = updateChecker
        self.diagnosticLogger = diagnosticLogger
        self.randomDouble = randomDouble
        self.drawDelayNanoseconds = drawDelayNanoseconds
        self.persistState = persistState
        self.userDefaults = userDefaults
        preferences = (initialPreferences ?? (persistState ? Self.loadPreferences(from: userDefaults) : SelectionPreferences())).normalized()
        history = initialHistory ?? (persistState ? Self.loadHistory(from: userDefaults) : [])
        language = initialLanguage ?? (persistState ? Self.loadLanguage(from: userDefaults) : AppLanguage.preferred())
        trimHistory()
        refreshSeededRandomFromPreferences()
        applyStatus()
    }

    var canDraw: Bool {
        !eligibleGames().isEmpty && !isDrawing && !isRefreshing
    }

    var canRefresh: Bool {
        !isRefreshing
    }

    var canLaunchSelectedGame: Bool {
        selectedGame.map(canLaunch) ?? false
    }

    var canInstallSelectedGame: Bool {
        selectedGame.map(canInstall) ?? false
    }

    var collectionOptions: [String] {
        var values: [String] = []
        for tag in library.flatMap(\.tags) {
            appendCollectionOption(tag, to: &values)
        }
        if let requiredCollection = preferences.filters.requiredCollection,
           !requiredCollection.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            appendCollectionOption(requiredCollection, to: &values)
        }
        return [text("Filters_NoCollection")] + values.sorted { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending }
    }

    var selectedGameTags: [String] {
        guard let selectedGame else { return [] }
        return displayTags(for: selectedGame)
    }

    func text(_ key: String) -> String {
        Localization.text(key, language: language)
    }

    func gameCount(_ count: Int) -> String {
        Localization.gameCount(count, language: language)
    }

    func refreshLibrary() async {
        guard !isRefreshing else { return }

        isRefreshing = true
        defer { isRefreshing = false }

        diagnosticLogger.info("Refreshing Steam library.")
        setStatus(.loadingLibrary)
        selectedGame = nil
        library = []

        do {
            let loadLibraryAction = loadLibraryAction
            library = try await Task.detached(priority: .userInitiated) {
                try loadLibraryAction()
            }.value
            diagnosticLogger.info("Loaded \(library.count) Steam library entries.")
            clearMissingCollectionFilterIfNeeded()
            updateEligibilitySummary()
        } catch {
            library = []
            selectedGame = nil
            preferences.filters.requiredCollection = nil
            diagnosticLogger.error("Steam library refresh failed: \(error.localizedDescription)")
            setStatus(.raw(error.localizedDescription))
        }
    }

    func checkForUpdates() async {
        await updateChecker.checkForUpdates()
    }

    func drawGame() async {
        guard !isDrawing, !isRefreshing else { return }

        if library.isEmpty {
            await refreshLibrary()
            guard !library.isEmpty else { return }
        }

        isDrawing = true
        defer { isDrawing = false }

        selectedGame = nil
        setStatus(.drawing)

        if drawDelayNanoseconds > 0 {
            try? await Task.sleep(nanoseconds: drawDelayNanoseconds)
        }

        let candidates = eligibleGames()
        guard !candidates.isEmpty else {
            updateEligibilitySummary()
            return
        }

        let selected = chooseGame(from: candidates)
        selectedGame = selected
        registerSelection(selected)
        setStatus(.drawn(selected.title))
        notificationSender.showGameSelected(selected, title: text("Notifications_GameDrawn"))
    }

    func openSelectedGame() {
        guard let game = selectedGame else {
            diagnosticLogger.error("Launch requested without a selected game.")
            setStatus(.raw(text("Status_NoGamesFound")))
            return
        }

        guard game.storefront == .steam else {
            diagnosticLogger.error("Launch unsupported for storefront \(game.storefront.rawValue).")
            setStatus(.raw(text("GameLaunch_UnsupportedStorefront")))
            return
        }

        guard let appId = game.steamAppId else {
            diagnosticLogger.error("Launch failed because the selected Steam app id is missing.")
            setStatus(.raw(text("GameLaunch_SteamMissingAppId")))
            return
        }

        guard canLaunch(game) else {
            diagnosticLogger.error("Launch blocked for non-installed Steam app \(appId).")
            setStatus(.raw(text("GameLaunch_LaunchNotInstalled")))
            return
        }

        openSteamURL("steam://run/\(appId)", action: "launch")
    }

    func installSelectedGame() {
        guard let game = selectedGame else {
            diagnosticLogger.error("Install requested without a selected game.")
            setStatus(.raw(text("Status_NoGamesFound")))
            return
        }

        guard game.storefront == .steam else {
            diagnosticLogger.error("Install unsupported for storefront \(game.storefront.rawValue).")
            setStatus(.raw(text("GameLaunch_UnsupportedStorefront")))
            return
        }

        guard let appId = game.steamAppId else {
            diagnosticLogger.error("Install failed because the selected Steam app id is missing.")
            setStatus(.raw(text("GameLaunch_SteamMissingAppId")))
            return
        }

        guard canInstall(game) else {
            diagnosticLogger.error("Install blocked because Steam app \(appId) is already installed.")
            setStatus(.raw(text("GameLaunch_SteamAlreadyInstalled")))
            return
        }

        openSteamURL("steam://install/\(appId)", action: "install")
    }

    func bindingForCategory(_ category: ProductCategory) -> Binding<Bool> {
        Binding(
            get: { self.preferences.filters.includedCategories.contains(category) },
            set: { isIncluded in
                if isIncluded {
                    self.preferences.filters.includedCategories.insert(category)
                } else {
                    self.preferences.filters.includedCategories.remove(category)
                }
            }
        )
    }

    func selectedCollectionBinding() -> Binding<String> {
        Binding(
            get: {
                self.preferences.filters.requiredCollection ?? self.text("Filters_NoCollection")
            },
            set: { value in
                let none = self.text("Filters_NoCollection")
                self.preferences.filters.requiredCollection = value == none || value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? nil : value
            }
        )
    }

    func eligibleGames() -> [GameEntry] {
        let excluded = excludedGameIds()
        return library.filter { game in
            if excluded.contains(game.id) {
                return false
            }

            if preferences.filters.requireInstalled && !(game.installState == .installed || game.installState == .shared) {
                return false
            }

            if preferences.filters.excludeDeckUnsupported && game.deckCompatibility == .unsupported {
                return false
            }

            if preferences.filters.requireMacCompatible && !game.supportedPlatforms.contains(.macOS) {
                return false
            }

            if preferences.filters.filterByStorefront && !preferences.filters.includedStorefronts.contains(game.storefront) {
                return false
            }

            let category = normalizedFilterCategory(game.productCategory)
            if !preferences.filters.includedCategories.contains(category) {
                return false
            }

            if let required = preferences.filters.requiredCollection, !required.isEmpty {
                return game.tags.contains { $0.caseInsensitiveCompare(required) == .orderedSame }
            }

            return true
        }
    }

    func installStateText(_ game: GameEntry) -> String {
        switch game.installState {
        case .installed:
            return text("GameDetails_InstallState_Installed")
        case .available:
            if game.ownershipType == .familyShared {
                return text("GameDetails_InstallState_FamilySharing")
            }
            return text("GameDetails_InstallState_Available")
        case .shared:
            return text("GameDetails_InstallState_FamilySharing")
        case .unknown:
            return text("GameDetails_InstallState_Unknown")
        }
    }

    func displayTags(for game: GameEntry) -> [String] {
        var values: [String] = []
        for tag in game.tags {
            appendCollectionOption(tag, to: &values)
        }
        return values
    }

    func canLaunch(_ game: GameEntry) -> Bool {
        game.storefront == .steam && game.steamAppId != nil && game.installState == .installed
    }

    func canInstall(_ game: GameEntry) -> Bool {
        guard game.storefront == .steam, game.steamAppId != nil else {
            return false
        }

        return game.installState == .available || game.installState == .shared || game.installState == .unknown
    }

    private func openSteamURL(_ value: String, action: String) {
        guard let url = URL(string: value) else { return }
        diagnosticLogger.info("Opening Steam \(action) URL: \(value)")
        if !openURL(url) {
            diagnosticLogger.error("Opening Steam \(action) URL failed: \(value)")
            setStatus(.raw(text("GameLaunch_OpenFailed")))
        }
    }

    private func normalizedFilterCategory(_ category: ProductCategory) -> ProductCategory {
        switch category {
        case .unknown:
            return .game
        case .dlc:
            return .other
        default:
            return category
        }
    }

    private func chooseGame(from candidates: [GameEntry]) -> GameEntry {
        let value = max(0, min(nextRandomDouble(), 0.999_999_999_999))
        let index = min(candidates.count - 1, Int((value * Double(candidates.count)).rounded(.down)))
        return candidates[index]
    }

    private func nextRandomDouble() -> Double {
        guard let seed = preferences.seed else {
            seededRandom = nil
            seededRandomSeed = nil
            seededRandomPosition = 0
            return randomDouble()
        }

        ensureSeededRandomInitialized(seed)
        let value = seededRandom?.nextDouble() ?? randomDouble()
        seededRandomPosition += 1
        preferences.randomPosition = seededRandomPosition
        return value
    }

    private func ensureSeededRandomInitialized(_ seed: Int) {
        if seededRandom == nil || seededRandomSeed != seed {
            refreshSeededRandomFromPreferences()
        } else if seededRandomPosition < preferences.randomPosition {
            advanceSeededRandom(to: preferences.randomPosition)
        } else if seededRandomPosition > preferences.randomPosition {
            seededRandom = DotNetCompatibleRandom(seed: seed)
            seededRandomSeed = seed
            seededRandomPosition = 0
            advanceSeededRandom(to: preferences.randomPosition)
        }
    }

    private func refreshSeededRandomFromPreferences() {
        guard let seed = preferences.seed else {
            seededRandom = nil
            seededRandomSeed = nil
            seededRandomPosition = 0
            return
        }

        seededRandom = DotNetCompatibleRandom(seed: seed)
        seededRandomSeed = seed
        seededRandomPosition = 0
        advanceSeededRandom(to: preferences.randomPosition)
    }

    private func advanceSeededRandom(to targetPosition: Int) {
        let target = max(0, targetPosition)
        while seededRandomPosition < target {
            _ = seededRandom?.nextDouble()
            seededRandomPosition += 1
        }
    }

    private func setStatus(_ state: StatusState) {
        statusState = state
        applyStatus()
    }

    private func applyStatus() {
        switch statusState {
        case .loadingLibrary:
            statusMessage = text("Status_LoadingLibrary")
        case .drawing:
            statusMessage = text("Status_Drawing")
        case let .drawn(title):
            statusMessage = Localization.format("Status_Drawn", language: language, title)
        case .noGamesFound:
            statusMessage = text("Status_NoGamesFound")
        case let .noMatches(total):
            statusMessage = Localization.format("Status_NoMatches", language: language, gameCount(total))
        case let .allEligible(total):
            statusMessage = Localization.format(
                countSensitiveStatusKey("Status_AllEligible", count: total),
                language: language,
                gameCount(total)
            )
        case let .filtered(eligible, total):
            statusMessage = Localization.format(
                countSensitiveStatusKey("Status_FilteredCount", count: eligible),
                language: language,
                gameCount(eligible),
                gameCount(total)
            )
        case let .raw(message):
            statusMessage = message
        }
    }

    private func countSensitiveStatusKey(_ prefix: String, count: Int) -> String {
        count == 1 ? "\(prefix)_Singular" : "\(prefix)_Plural"
    }

    private func appendCollectionOption(_ value: String, to values: inout [String]) {
        let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return }
        if values.contains(where: { $0.caseInsensitiveCompare(trimmed) == .orderedSame }) {
            return
        }
        values.append(trimmed)
    }

    private func updateEligibilitySummary() {
        let eligible = eligibleGames()
        let total = library.count

        if let selectedGame, !eligible.contains(where: { $0.id == selectedGame.id }) {
            self.selectedGame = nil
        }

        if total == 0 {
            setStatus(.noGamesFound)
        } else if eligible.isEmpty {
            setStatus(.noMatches(total: total))
        } else if eligible.count == total {
            setStatus(.allEligible(total: total))
        } else {
            setStatus(.filtered(eligible: eligible.count, total: total))
        }
    }

    private func clearMissingCollectionFilterIfNeeded() {
        guard let required = preferences.filters.requiredCollection, !required.isEmpty else {
            return
        }

        var matchingCollection: String?
        for game in library {
            if let tag = game.tags.first(where: { $0.caseInsensitiveCompare(required) == .orderedSame }) {
                matchingCollection = tag
                break
            }
        }

        if let matchingCollection {
            if matchingCollection != required {
                preferences.filters.requiredCollection = matchingCollection
            }
        } else {
            preferences.filters.requiredCollection = nil
        }
    }

    private func excludedGameIds() -> Set<String> {
        let count = max(0, min(preferences.recentGameExclusionCount, history.count))
        guard count > 0 else { return [] }
        return Set(history.suffix(count).map(\.gameId))
    }

    private func registerSelection(_ game: GameEntry) {
        let limit = max(0, preferences.historyLimit)
        if limit == 0 {
            history = []
        } else {
            while history.count >= limit {
                history.removeFirst()
            }
            history.append(SelectionHistoryEntry(gameId: game.id, title: game.title, selectedAt: Date()))
        }
        saveHistory()
    }

    private func trimHistory() {
        let limit = max(0, preferences.historyLimit)
        if limit == 0 {
            if !history.isEmpty {
                history = []
                saveHistory()
            }
            return
        }

        guard history.count > limit else { return }
        history.removeFirst(history.count - limit)
        saveHistory()
    }

    private func savePreferences() {
        guard persistState else { return }
        guard let data = try? JSONEncoder().encode(preferences) else { return }
        userDefaults.set(data, forKey: Self.preferencesKey)
    }

    private func saveHistory() {
        guard persistState else { return }
        guard let data = try? JSONEncoder().encode(history) else { return }
        userDefaults.set(data, forKey: Self.historyKey)
    }

    private static func loadPreferences(from userDefaults: UserDefaults) -> SelectionPreferences {
        guard
            let data = userDefaults.data(forKey: preferencesKey),
            let decoded = try? JSONDecoder().decode(SelectionPreferences.self, from: data)
        else {
            return SelectionPreferences()
        }
        return decoded
    }

    private static func loadLanguage(from userDefaults: UserDefaults) -> AppLanguage {
        if let code = userDefaults.string(forKey: languageKey), let saved = AppLanguage(rawValue: code) {
            return saved
        }

        return AppLanguage.preferred()
    }

    private static func loadHistory(from userDefaults: UserDefaults) -> [SelectionHistoryEntry] {
        guard
            let data = userDefaults.data(forKey: historyKey),
            let decoded = try? JSONDecoder().decode([SelectionHistoryEntry].self, from: data)
        else {
            return []
        }
        return decoded
    }
}
