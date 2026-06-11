import XCTest
@testable import SteamBacklogPickerMac

@MainActor
final class SelectionFilterTests: XCTestCase {
    func testEligibleGamesRespectInstalledCategoryAndCollectionFilters() {
        let store = makeStore()
        store.library = [
            GameEntry(
                storefront: .steam,
                steamAppId: 1,
                title: "Installed RPG",
                ownershipType: .owned,
                installState: .installed,
                productCategory: .game,
                sizeOnDisk: 10,
                lastPlayed: nil,
                tags: ["RPG"],
                deckCompatibility: .unknown,
                coverURL: nil
            ),
            GameEntry(
                storefront: .steam,
                steamAppId: 2,
                title: "Tool",
                ownershipType: .owned,
                installState: .available,
                productCategory: .tool,
                sizeOnDisk: nil,
                lastPlayed: nil,
                tags: ["RPG"],
                deckCompatibility: .unknown,
                coverURL: nil
            ),
            GameEntry(
                storefront: .steam,
                steamAppId: 3,
                title: "Other Game",
                ownershipType: .owned,
                installState: .installed,
                productCategory: .game,
                sizeOnDisk: 20,
                lastPlayed: nil,
                tags: ["Action"],
                deckCompatibility: .unknown,
                coverURL: nil
            )
        ]

        store.preferences.filters.requireInstalled = true
        store.preferences.filters.requiredCollection = "RPG"
        store.preferences.filters.includedCategories = [.game]

        XCTAssertEqual(store.eligibleGames().map(\.title), ["Installed RPG"])
    }

    func testEligibleGamesRespectCategoryStorefrontDeckAndCollectionFilters() {
        let store = makeStore()
        store.library = [
            makeGame(appId: 10, title: "Deck Ready Game", installState: .available, tags: ["Backlog"], deckCompatibility: .playable),
            makeGame(appId: 20, title: "Unsupported Game", installState: .available, tags: ["Backlog"], deckCompatibility: .unsupported),
            makeGame(appId: 30, title: "Tool", installState: .available, productCategory: .tool, tags: ["Backlog"], deckCompatibility: .verified),
            GameEntry(
                storefront: .unknown,
                steamAppId: nil,
                title: "External Game",
                ownershipType: .owned,
                installState: .available,
                productCategory: .game,
                sizeOnDisk: nil,
                lastPlayed: nil,
                tags: ["Backlog"],
                deckCompatibility: .verified,
                coverURL: nil
            )
        ]

        store.preferences.filters.requireInstalled = false
        store.preferences.filters.excludeDeckUnsupported = true
        store.preferences.filters.filterByStorefront = true
        store.preferences.filters.includedStorefronts = [.steam]
        store.preferences.filters.includedCategories = [.game]
        store.preferences.filters.requiredCollection = "Backlog"

        XCTAssertEqual(store.eligibleGames().map(\.title), ["Deck Ready Game"])

        store.preferences.filters.includedCategories = []

        XCTAssertTrue(store.eligibleGames().isEmpty)
    }

    func testEligibleGamesRespectEachContentTypeFilter() {
        let store = makeStore()
        store.library = [
            makeGame(appId: 10, title: "Game", installState: .available, productCategory: .game),
            makeGame(appId: 20, title: "Soundtrack", installState: .available, productCategory: .soundtrack),
            makeGame(appId: 30, title: "Software", installState: .available, productCategory: .software),
            makeGame(appId: 40, title: "Tool", installState: .available, productCategory: .tool),
            makeGame(appId: 50, title: "Video", installState: .available, productCategory: .video),
            makeGame(appId: 60, title: "Other", installState: .available, productCategory: .other),
            makeGame(appId: 70, title: "Legacy DLC", installState: .available, productCategory: .dlc),
            makeGame(appId: 80, title: "Unknown Defaults To Game", installState: .available, productCategory: .unknown)
        ]

        assertEligibleTitles(["Game", "Unknown Defaults To Game"], for: [.game], store: store)
        assertEligibleTitles(["Soundtrack"], for: [.soundtrack], store: store)
        assertEligibleTitles(["Software"], for: [.software], store: store)
        assertEligibleTitles(["Tool"], for: [.tool], store: store)
        assertEligibleTitles(["Video"], for: [.video], store: store)
        assertEligibleTitles(["Other", "Legacy DLC"], for: [.other], store: store)
    }

    func testChangingFiltersClearsSelectedGameWhenItIsNoLongerEligible() {
        let store = makeStore()
        let game = makeGame(appId: 10, title: "Selected Game", installState: .available, productCategory: .game)
        let tool = makeGame(appId: 20, title: "Remaining Tool", installState: .available, productCategory: .tool)
        store.library = [game, tool]
        store.preferences.filters.includedCategories = [.game, .tool]
        store.selectedGame = game

        store.preferences.filters.includedCategories = [.tool]

        XCTAssertNil(store.selectedGame)
        XCTAssertEqual(store.eligibleGames().map(\.title), ["Remaining Tool"])
        XCTAssertEqual(store.statusMessage, "1 game available after applying filters (of 2 games).")
    }

    func testLaunchAndInstallAvailabilityMatchesSteamInstallStateRules() {
        let store = makeStore()

        let installed = makeGame(appId: 10, installState: .installed)
        let available = makeGame(appId: 20, installState: .available)
        let shared = makeGame(appId: 30, installState: .shared)
        let unknown = makeGame(appId: 40, installState: .unknown)
        let missingAppId = makeGame(appId: nil, installState: .available)

        XCTAssertTrue(store.canLaunch(installed))
        XCTAssertFalse(store.canInstall(installed))

        XCTAssertFalse(store.canLaunch(available))
        XCTAssertTrue(store.canInstall(available))

        XCTAssertFalse(store.canLaunch(shared))
        XCTAssertTrue(store.canInstall(shared))

        XCTAssertFalse(store.canLaunch(unknown))
        XCTAssertTrue(store.canInstall(unknown))

        XCTAssertFalse(store.canLaunch(missingAppId))
        XCTAssertFalse(store.canInstall(missingAppId))
    }

    func testInstallStateTextMatchesSharedViewModelRules() {
        let store = makeStore(initialLanguage: .english)

        XCTAssertEqual(store.installStateText(makeGame(appId: 10, installState: .installed)), "Installed")
        XCTAssertEqual(store.installStateText(makeGame(appId: 20, installState: .available)), "Available to install")
        XCTAssertEqual(
            store.installStateText(makeGame(appId: 30, installState: .available, ownershipType: .familyShared)),
            "Available via family sharing"
        )
        XCTAssertEqual(store.installStateText(makeGame(appId: 40, installState: .shared)), "Available via family sharing")
        XCTAssertEqual(store.installStateText(makeGame(appId: 50, installState: .unknown)), "Installation status unknown")
    }

    func testDisplayedTagsMatchSharedGameDetailsNormalization() {
        let store = makeStore(initialLanguage: .english)
        let game = makeGame(
            appId: 10,
            installState: .available,
            tags: ["RPG", " rpg ", "", "Backlog", "BACKLOG", "  "]
        )
        store.selectedGame = game

        XCTAssertEqual(store.displayTags(for: game), ["RPG", "Backlog"])
        XCTAssertEqual(store.selectedGameTags, ["RPG", "Backlog"])
    }

    func testInstallAndPlayCommandsOpenExpectedSteamUrls() {
        var openedURLs: [URL] = []
        let diagnostics = FakeDiagnosticLogger()
        let store = makeStore(
            openURL: {
                openedURLs.append($0)
                return true
            },
            diagnosticLogger: diagnostics
        )

        store.selectedGame = makeGame(appId: 20, installState: .available)
        store.installSelectedGame()

        store.selectedGame = makeGame(appId: 10, installState: .installed)
        store.openSelectedGame()

        XCTAssertEqual(openedURLs.map(\.absoluteString), [
            "steam://install/20",
            "steam://run/10"
        ])
        XCTAssertEqual(diagnostics.infoMessages, [
            "Opening Steam install URL: steam://install/20",
            "Opening Steam launch URL: steam://run/10"
        ])
    }

    func testLaunchAndInstallFailuresMatchSharedLaunchMessages() {
        var openedURLs: [URL] = []
        let diagnostics = FakeDiagnosticLogger()
        let store = makeStore(
            openURL: {
                openedURLs.append($0)
                return true
            },
            diagnosticLogger: diagnostics,
            initialLanguage: .english
        )

        store.selectedGame = makeGame(appId: 10, storefront: .unknown, installState: .installed)
        store.openSelectedGame()
        XCTAssertEqual(store.statusMessage, "This storefront does not support launching from SteamBacklog Picker yet.")

        store.installSelectedGame()
        XCTAssertEqual(store.statusMessage, "This storefront does not support launching from SteamBacklog Picker yet.")

        store.selectedGame = makeGame(appId: nil, installState: .available)
        store.openSelectedGame()
        XCTAssertEqual(store.statusMessage, "The Steam app identifier is missing for this game.")

        store.selectedGame = makeGame(appId: 20, installState: .available)
        store.openSelectedGame()
        XCTAssertEqual(store.statusMessage, "Install the game before launching it.")

        store.selectedGame = makeGame(appId: 30, installState: .installed)
        store.installSelectedGame()
        XCTAssertEqual(store.statusMessage, "The game is already installed via Steam.")

        XCTAssertTrue(openedURLs.isEmpty)
        XCTAssertEqual(diagnostics.errorMessages, [
            "Launch unsupported for storefront unknown.",
            "Install unsupported for storefront unknown.",
            "Launch failed because the selected Steam app id is missing.",
            "Launch blocked for non-installed Steam app 20.",
            "Install blocked because Steam app 30 is already installed."
        ])
    }

    func testSteamUrlOpenFailuresAreShownAndLogged() {
        var openedURLs: [URL] = []
        let diagnostics = FakeDiagnosticLogger()
        let store = makeStore(
            openURL: {
                openedURLs.append($0)
                return false
            },
            diagnosticLogger: diagnostics,
            initialLanguage: .english
        )

        store.selectedGame = makeGame(appId: 10, installState: .installed)
        store.openSelectedGame()

        XCTAssertEqual(openedURLs.map(\.absoluteString), ["steam://run/10"])
        XCTAssertEqual(store.statusMessage, "Could not open the Steam link on this Mac.")
        XCTAssertEqual(diagnostics.infoMessages, ["Opening Steam launch URL: steam://run/10"])
        XCTAssertEqual(diagnostics.errorMessages, ["Opening Steam launch URL failed: steam://run/10"])
    }

    func testCheckForUpdatesUsesInjectedUpdateChecker() async {
        let updateChecker = FakeUpdateChecker()
        let store = makeStore(updateChecker: updateChecker)

        await store.checkForUpdates()

        XCTAssertEqual(updateChecker.callCount, 1)
    }

    func testRefreshLibraryUsesInjectedLoaderAndUpdatesEligibilitySummary() async {
        let loadedGames = [
            makeGame(appId: 10, title: "Injected Game", installState: .available),
            makeGame(appId: 20, title: "Injected Tool", installState: .available, productCategory: .tool)
        ]
        let diagnostics = FakeDiagnosticLogger()
        let store = makeStore(
            loadLibrary: { loadedGames },
            diagnosticLogger: diagnostics,
            initialLanguage: .english
        )

        await store.refreshLibrary()

        XCTAssertEqual(store.library.map(\.title), ["Injected Game", "Injected Tool"])
        XCTAssertEqual(store.statusMessage, "1 game available after applying filters (of 2 games).")
        XCTAssertEqual(diagnostics.infoMessages, [
            "Refreshing Steam library.",
            "Loaded 2 Steam library entries."
        ])
    }

    func testRefreshLibraryLogsLoadFailures() async {
        struct LoadFailure: LocalizedError {
            var errorDescription: String? { "Steam metadata is unreadable." }
        }

        let diagnostics = FakeDiagnosticLogger()
        var preferences = SelectionPreferences()
        preferences.filters.requiredCollection = "Roletada"
        let store = makeStore(
            loadLibrary: { throw LoadFailure() },
            diagnosticLogger: diagnostics,
            initialPreferences: preferences,
            initialLanguage: .english
        )

        await store.refreshLibrary()

        XCTAssertTrue(store.library.isEmpty)
        XCTAssertNil(store.preferences.filters.requiredCollection)
        XCTAssertEqual(store.statusMessage, "Steam metadata is unreadable.")
        XCTAssertEqual(diagnostics.infoMessages, ["Refreshing Steam library."])
        XCTAssertEqual(diagnostics.errorMessages, ["Steam library refresh failed: Steam metadata is unreadable."])
    }

    func testRefreshLibraryIgnoresDuplicateRequestsWhileRefreshing() async {
        let gate = RefreshGate()
        let loadedGame = makeGame(appId: 10, title: "Loaded Once", installState: .available)
        let store = makeStore(
            loadLibrary: {
                gate.recordCallAndWait()
                return [loadedGame]
            },
            initialLanguage: .english
        )

        let firstRefresh = Task {
            await store.refreshLibrary()
        }

        while !store.isRefreshing {
            await Task.yield()
        }

        XCTAssertFalse(store.canRefresh)

        await store.refreshLibrary()
        gate.release()
        await firstRefresh.value

        XCTAssertTrue(store.canRefresh)
        XCTAssertEqual(gate.callCount, 1)
        XCTAssertEqual(store.library.map(\.title), ["Loaded Once"])
    }

    func testDrawIsDisabledAndIgnoredWhileRefreshing() async {
        let gate = RefreshGate()
        var randomCallCount = 0
        let notifications = FakeNotificationSender()
        let freshGame = makeGame(appId: 20, title: "Fresh Game", installState: .available)
        let store = makeStore(
            loadLibrary: {
                gate.recordCallAndWait()
                return [freshGame]
            },
            notificationSender: notifications,
            randomDouble: {
                randomCallCount += 1
                return 0
            },
            initialLanguage: .english
        )
        store.library = [
            makeGame(appId: 10, title: "Stale Game", installState: .available)
        ]

        let refreshTask = Task {
            await store.refreshLibrary()
        }

        while !store.isRefreshing {
            await Task.yield()
        }

        XCTAssertFalse(store.canDraw)
        XCTAssertTrue(store.library.isEmpty)

        await store.drawGame()
        XCTAssertNil(store.selectedGame)
        XCTAssertEqual(randomCallCount, 0)
        XCTAssertTrue(notifications.shown.isEmpty)

        gate.release()
        await refreshTask.value

        XCTAssertEqual(store.library.map(\.title), ["Fresh Game"])
        XCTAssertTrue(store.canDraw)
    }

    func testRefreshLibraryClearsMissingCollectionFilter() async {
        var preferences = SelectionPreferences()
        preferences.filters.requiredCollection = "Missing Collection"
        preferences.filters.includedCategories = [.game]
        let loadedGame = makeGame(appId: 10, title: "Loaded Game", installState: .available, tags: ["Existing Collection"])
        let store = makeStore(
            loadLibrary: {
                [loadedGame]
            },
            initialPreferences: preferences,
            initialLanguage: .english
        )

        await store.refreshLibrary()

        XCTAssertNil(store.preferences.filters.requiredCollection)
        XCTAssertEqual(store.eligibleGames().map(\.title), ["Loaded Game"])
        XCTAssertEqual(store.statusMessage, "1 game available to draw.")
    }

    func testRefreshLibraryPreservesAndCanonicalizesPersistedCollectionSelection() async {
        var preferences = SelectionPreferences()
        preferences.filters.requiredCollection = "roletada"
        preferences.filters.includedCategories = [.game]
        let loadedGame = makeGame(appId: 10, title: "Loaded Game", installState: .available, tags: ["Roletada"])
        let store = makeStore(
            loadLibrary: {
                [loadedGame]
            },
            initialPreferences: preferences,
            initialLanguage: .english
        )

        XCTAssertEqual(store.collectionOptions, ["No collection", "roletada"])

        await store.refreshLibrary()

        XCTAssertEqual(store.preferences.filters.requiredCollection, "Roletada")
        XCTAssertEqual(store.collectionOptions, ["No collection", "Roletada"])
        XCTAssertEqual(store.eligibleGames().map(\.title), ["Loaded Game"])
    }

    func testCollectionOptionsDeduplicateCaseInsensitiveAndTrimNames() {
        var preferences = SelectionPreferences()
        preferences.filters.requiredCollection = " favorites "
        let store = makeStore(initialPreferences: preferences, initialLanguage: .english)
        store.library = [
            makeGame(appId: 10, title: "First", installState: .available, tags: ["Favorites", "  favorites  "]),
            makeGame(appId: 20, title: "Second", installState: .available, tags: ["FAVORITES", "Backlog"])
        ]

        XCTAssertEqual(store.collectionOptions, ["No collection", "Backlog", "Favorites"])
    }

    func testPreferencesNormalizeLegacyValues() {
        var preferences = SelectionPreferences()
        preferences.filters.requiredCollection = "  Favorites  "
        preferences.filters.includedCategories = [.unknown, .dlc, .other]
        preferences.filters.includedStorefronts = [.unknown, .steam]
        preferences.recentGameExclusionCount = -5
        preferences.historyLimit = -1

        let store = makeStore(initialPreferences: preferences)

        XCTAssertEqual(store.preferences.filters.requiredCollection, "Favorites")
        XCTAssertEqual(store.preferences.filters.includedCategories, [.game, .other])
        XCTAssertEqual(store.preferences.filters.includedStorefronts, [.steam])
        XCTAssertEqual(store.preferences.recentGameExclusionCount, 0)
        XCTAssertEqual(store.preferences.historyLimit, 0)
    }

    func testPersistedPreferencesRestoreContentFiltersLanguageAndHistory() async {
        let suiteName = "SteamBacklogPickerMacTests-\(UUID().uuidString)"
        guard let userDefaults = UserDefaults(suiteName: suiteName) else {
            XCTFail("Could not create isolated user defaults suite.")
            return
        }
        userDefaults.removePersistentDomain(forName: suiteName)
        defer {
            userDefaults.removePersistentDomain(forName: suiteName)
        }

        let firstStore = makeStore(
            randomDouble: { 0 },
            initialLanguage: .english,
            persistState: true,
            userDefaults: userDefaults
        )
        firstStore.library = [
            makeGame(appId: 10, title: "Game", installState: .available, productCategory: .game),
            makeGame(appId: 20, title: "Software", installState: .available, productCategory: .software),
            makeGame(appId: 30, title: "Video", installState: .available, productCategory: .video)
        ]
        firstStore.preferences.filters.includedCategories = [.software, .video]
        firstStore.preferences.recentGameExclusionCount = 1
        firstStore.language = .portuguese

        await firstStore.drawGame()
        XCTAssertEqual(firstStore.selectedGame?.title, "Software")

        let restoredStore = makeStore(
            randomDouble: { 0 },
            initialLanguage: nil,
            persistState: true,
            userDefaults: userDefaults
        )
        restoredStore.library = firstStore.library

        XCTAssertEqual(restoredStore.language, .portuguese)
        XCTAssertEqual(restoredStore.preferences.filters.includedCategories, [.software, .video])
        XCTAssertEqual(restoredStore.eligibleGames().map(\.title), ["Video"])
    }

    func testPartialPersistedPreferencesDecodeWithDefaultsAndNormalize() {
        let suiteName = "SteamBacklogPickerMacTests-\(UUID().uuidString)"
        guard let userDefaults = UserDefaults(suiteName: suiteName) else {
            XCTFail("Could not create isolated user defaults suite.")
            return
        }
        userDefaults.removePersistentDomain(forName: suiteName)
        defer {
            userDefaults.removePersistentDomain(forName: suiteName)
        }

        let legacyJSON = """
        {
          "filters": {
            "requiredCollection": "  Favorites  ",
            "includedCategories": ["unknown", "dlc"]
          },
          "randomPosition": -7,
          "recentGameExclusionCount": -3,
          "historyLimit": -1
        }
        """
        userDefaults.set(Data(legacyJSON.utf8), forKey: "SteamBacklogPicker.SelectionPreferences")

        let store = makeStore(
            initialLanguage: .english,
            persistState: true,
            userDefaults: userDefaults
        )

        XCTAssertFalse(store.preferences.filters.requireInstalled)
        XCTAssertFalse(store.preferences.filters.excludeDeckUnsupported)
        XCTAssertEqual(store.preferences.filters.requiredCollection, "Favorites")
        XCTAssertEqual(store.preferences.filters.includedCategories, [.game, .other])
        XCTAssertFalse(store.preferences.filters.filterByStorefront)
        XCTAssertTrue(store.preferences.filters.includedStorefronts.isEmpty)
        XCTAssertEqual(store.preferences.randomPosition, 0)
        XCTAssertEqual(store.preferences.recentGameExclusionCount, 0)
        XCTAssertEqual(store.preferences.historyLimit, 0)

        store.library = [makeGame(appId: 10, title: "Steam Game", installState: .available, tags: ["Favorites"])]
        XCTAssertEqual(store.eligibleGames().map(\.title), ["Steam Game"])
    }

    func testPortugueseFilteredStatusUsesSingularAvailabilityText() async {
        let loadedGames = [
            makeGame(appId: 10, title: "Jogo", installState: .available),
            makeGame(appId: 20, title: "Ferramenta", installState: .available, productCategory: .tool)
        ]
        let store = makeStore(
            loadLibrary: { loadedGames },
            initialLanguage: .portuguese
        )

        await store.refreshLibrary()

        XCTAssertEqual(store.statusMessage, "1 jogo disponível após aplicar os filtros (de 2 jogos).")
    }

    func testDrawPreservesDrawnStatusWhenLanguageChanges() async {
        let store = makeStore(
            randomDouble: { 0 },
            initialLanguage: .english
        )
        store.library = [
            makeGame(appId: 10, title: "Localized Game", installState: .available)
        ]

        await store.drawGame()
        XCTAssertEqual(store.statusMessage, "Drawn game: Localized Game")

        store.language = .portuguese

        XCTAssertEqual(store.statusMessage, "Jogo sorteado: Localized Game")
    }

    func testLanguageChangeNotifiesShellForMenuRefresh() {
        let store = makeStore(initialLanguage: .english)
        var notificationCount = 0
        store.languageDidChange = {
            notificationCount += 1
        }

        store.language = .portuguese

        XCTAssertEqual(notificationCount, 1)
        XCTAssertEqual(store.text("Filters_DrawButton"), "Sortear")
        XCTAssertEqual(store.text("Menu_File"), "Arquivo")
    }

    func testDrawShowsGameSelectedNotification() async {
        let notifications = FakeNotificationSender()
        let store = makeStore(
            notificationSender: notifications,
            randomDouble: { 0 },
            initialLanguage: .english
        )
        store.library = [
            makeGame(appId: 10, title: "Notified Game", installState: .available)
        ]

        await store.drawGame()

        XCTAssertEqual(notifications.shown.map(\.title), ["Game drawn!"])
        XCTAssertEqual(notifications.shown.map(\.game.title), ["Notified Game"])
    }

    func testDrawRespectsRecentGameExclusionCount() async {
        var randomValues = [0.0, 0.0]
        var preferences = SelectionPreferences()
        preferences.recentGameExclusionCount = 1
        preferences.historyLimit = 10
        let store = makeStore(
            randomDouble: {
                randomValues.removeFirst()
            },
            initialPreferences: preferences
        )
        store.library = [
            makeGame(appId: 10, title: "First Game", installState: .available),
            makeGame(appId: 20, title: "Second Game", installState: .available)
        ]

        await store.drawGame()
        XCTAssertEqual(store.selectedGame?.title, "First Game")

        await store.drawGame()

        XCTAssertEqual(store.selectedGame?.title, "Second Game")
    }

    func testDrawBecomesUnavailableWhenRecentHistoryExcludesOnlyCandidate() async {
        var preferences = SelectionPreferences()
        preferences.recentGameExclusionCount = 1
        preferences.historyLimit = 10
        let store = makeStore(
            randomDouble: { 0 },
            initialPreferences: preferences
        )
        store.library = [
            makeGame(appId: 10, title: "Only Candidate", installState: .available)
        ]

        XCTAssertTrue(store.canDraw)

        await store.drawGame()

        XCTAssertEqual(store.selectedGame?.title, "Only Candidate")
        XCTAssertFalse(store.canDraw)
        XCTAssertTrue(store.eligibleGames().isEmpty)
    }

    func testChangingHistoryLimitImmediatelyTrimsRecentExclusions() {
        var preferences = SelectionPreferences()
        preferences.recentGameExclusionCount = 2
        preferences.historyLimit = 2
        let firstGame = makeGame(appId: 10, title: "First Game", installState: .available)
        let secondGame = makeGame(appId: 20, title: "Second Game", installState: .available)
        let store = makeStore(
            initialPreferences: preferences,
            initialHistory: [
                SelectionHistoryEntry(gameId: firstGame.id, title: firstGame.title, selectedAt: Date()),
                SelectionHistoryEntry(gameId: secondGame.id, title: secondGame.title, selectedAt: Date())
            ]
        )
        store.library = [firstGame, secondGame]

        XCTAssertTrue(store.eligibleGames().isEmpty)

        store.preferences.historyLimit = 1

        XCTAssertEqual(store.eligibleGames().map(\.title), ["First Game"])
    }

    func testLoadedHistoryIsTrimmedToStoredHistoryLimit() {
        var preferences = SelectionPreferences()
        preferences.recentGameExclusionCount = 2
        preferences.historyLimit = 1
        let firstGame = makeGame(appId: 10, title: "First Game", installState: .available)
        let secondGame = makeGame(appId: 20, title: "Second Game", installState: .available)
        let store = makeStore(
            initialPreferences: preferences,
            initialHistory: [
                SelectionHistoryEntry(gameId: firstGame.id, title: firstGame.title, selectedAt: Date()),
                SelectionHistoryEntry(gameId: secondGame.id, title: secondGame.title, selectedAt: Date())
            ]
        )
        store.library = [firstGame, secondGame]

        XCTAssertEqual(store.eligibleGames().map(\.title), ["First Game"])
    }

    func testDrawUsesDotNetCompatibleSeededSequence() async {
        var preferences = SelectionPreferences()
        preferences.seed = 13579
        preferences.historyLimit = 20
        preferences.recentGameExclusionCount = 0
        let store = makeStore(
            randomDouble: { 0.999 },
            initialPreferences: preferences
        )
        store.library = seededSequenceGames()

        var selectedAppIds: [UInt32] = []
        for _ in 0..<10 {
            await store.drawGame()
            selectedAppIds.append(store.selectedGame!.steamAppId!)
        }

        XCTAssertEqual(selectedAppIds, [30, 10, 20, 40, 40, 10, 10, 40, 40, 30])
        XCTAssertEqual(store.preferences.randomPosition, 10)
    }

    func testDrawResumesSeededSequenceFromStoredRandomPosition() async {
        var preferences = SelectionPreferences()
        preferences.seed = 13579
        preferences.historyLimit = 20
        preferences.recentGameExclusionCount = 0
        let firstStore = makeStore(initialPreferences: preferences)
        firstStore.library = seededSequenceGames()

        var firstBatch: [UInt32] = []
        for _ in 0..<6 {
            await firstStore.drawGame()
            firstBatch.append(firstStore.selectedGame!.steamAppId!)
        }

        let resumedStore = makeStore(initialPreferences: firstStore.preferences)
        resumedStore.library = seededSequenceGames()

        var resumedBatch: [UInt32] = []
        for _ in 0..<4 {
            await resumedStore.drawGame()
            resumedBatch.append(resumedStore.selectedGame!.steamAppId!)
        }

        XCTAssertEqual(firstBatch + resumedBatch, [30, 10, 20, 40, 40, 10, 10, 40, 40, 30])
        XCTAssertEqual(resumedStore.preferences.randomPosition, 10)
    }

    func testChangingSeedResetsStoredRandomPosition() {
        var preferences = SelectionPreferences()
        preferences.seed = 13579
        preferences.randomPosition = 6
        let store = makeStore(initialPreferences: preferences)

        store.preferences.seed = 9876

        XCTAssertEqual(store.preferences.randomPosition, 0)
    }

    func testDrawUsesLatestFiltersAfterDrawingDelay() async {
        let store = makeStore(
            randomDouble: { 0 },
            drawDelayNanoseconds: 50_000_000
        )
        store.library = [
            makeGame(appId: 10, title: "Game", installState: .available),
            makeGame(appId: 20, title: "Tool", installState: .available, productCategory: .tool)
        ]
        store.preferences.filters.includedCategories = [.game]

        let drawTask = Task { await store.drawGame() }
        while !store.isDrawing {
            await Task.yield()
        }

        store.preferences.filters.includedCategories = [.tool]

        await drawTask.value

        XCTAssertEqual(store.selectedGame?.title, "Tool")
    }

    func testDrawIgnoresDuplicateRequestsWhileDrawing() async {
        var randomCallCount = 0
        let notifications = FakeNotificationSender()
        let store = makeStore(
            notificationSender: notifications,
            randomDouble: {
                randomCallCount += 1
                return 0
            },
            drawDelayNanoseconds: 50_000_000
        )
        store.library = [
            makeGame(appId: 10, title: "Single Draw", installState: .available)
        ]

        let drawTask = Task { await store.drawGame() }
        while !store.isDrawing {
            await Task.yield()
        }

        await store.drawGame()
        await drawTask.value

        XCTAssertEqual(store.selectedGame?.title, "Single Draw")
        XCTAssertEqual(randomCallCount, 1)
        XCTAssertEqual(notifications.shown.count, 1)
    }

    private func makeGame(
        appId: UInt32?,
        title: String? = nil,
        storefront: Storefront = .steam,
        installState: InstallState,
        ownershipType: OwnershipType? = nil,
        productCategory: ProductCategory = .game,
        tags: [String] = [],
        deckCompatibility: SteamDeckCompatibility = .unknown
    ) -> GameEntry {
        GameEntry(
            storefront: storefront,
            steamAppId: appId,
            title: title ?? "Game \(appId.map(String.init) ?? "missing")",
            ownershipType: ownershipType ?? (installState == .shared ? .familyShared : .owned),
            installState: installState,
            productCategory: productCategory,
            sizeOnDisk: nil,
            lastPlayed: nil,
            tags: tags,
            deckCompatibility: deckCompatibility,
            coverURL: nil
        )
    }

    private func assertEligibleTitles(_ expected: [String], for categories: Set<ProductCategory>, store: AppStore) {
        store.preferences.filters.includedCategories = categories
        XCTAssertEqual(store.eligibleGames().map(\.title), expected)
    }

    private func seededSequenceGames() -> [GameEntry] {
        [
            makeGame(appId: 10, title: "First", installState: .available),
            makeGame(appId: 20, title: "Second", installState: .available),
            makeGame(appId: 30, title: "Third", installState: .available),
            makeGame(appId: 40, title: "Fourth", installState: .available)
        ]
    }

    private func makeStore(
        loadLibrary: @escaping @Sendable () throws -> [GameEntry] = { [] },
        openURL: @escaping (URL) -> Bool = { _ in true },
        notificationSender: GameNotificationSending = NullGameNotificationService(),
        updateChecker: AppUpdateChecking = NoOpMacAppUpdateService(),
        diagnosticLogger: DiagnosticLogging = NullDiagnosticLogger(),
        randomDouble: @escaping () -> Double = { 0 },
        drawDelayNanoseconds: UInt64 = 0,
        initialPreferences: SelectionPreferences? = nil,
        initialHistory: [SelectionHistoryEntry]? = nil,
        initialLanguage: AppLanguage? = .english,
        persistState: Bool = false,
        userDefaults: UserDefaults = .standard
    ) -> AppStore {
        AppStore(
            loadLibrary: loadLibrary,
            openURL: openURL,
            notificationSender: notificationSender,
            updateChecker: updateChecker,
            diagnosticLogger: diagnosticLogger,
            randomDouble: randomDouble,
            drawDelayNanoseconds: drawDelayNanoseconds,
            initialPreferences: initialPreferences,
            initialHistory: initialHistory,
            initialLanguage: initialLanguage,
            persistState: persistState,
            userDefaults: userDefaults
        )
    }

    private final class FakeNotificationSender: GameNotificationSending {
        var shown: [(game: GameEntry, title: String)] = []

        func showGameSelected(_ game: GameEntry, title: String) {
            shown.append((game, title))
        }
    }

    private final class RefreshGate: @unchecked Sendable {
        private let semaphore = DispatchSemaphore(value: 0)
        private let lock = NSLock()
        private var calls = 0

        var callCount: Int {
            lock.lock()
            defer { lock.unlock() }
            return calls
        }

        func recordCallAndWait() {
            lock.lock()
            calls += 1
            lock.unlock()
            semaphore.wait()
        }

        func release() {
            semaphore.signal()
        }
    }

    private final class FakeUpdateChecker: AppUpdateChecking {
        private(set) var callCount = 0

        func checkForUpdates() async {
            callCount += 1
        }
    }

    private final class FakeDiagnosticLogger: DiagnosticLogging {
        private(set) var infoMessages: [String] = []
        private(set) var errorMessages: [String] = []

        func info(_ message: String) {
            infoMessages.append(message)
        }

        func error(_ message: String) {
            errorMessages.append(message)
        }
    }
}
