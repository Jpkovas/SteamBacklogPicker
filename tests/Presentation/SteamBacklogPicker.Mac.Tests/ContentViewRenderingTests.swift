import AppKit
import SwiftUI
import XCTest
@testable import SteamBacklogPickerMac

@MainActor
final class ContentViewRenderingTests: XCTestCase {
    func testContentViewRendersNonBlankDefaultWindow() throws {
        let store = AppStore(
            loadLibrary: { [] },
            openURL: { _ in true },
            notificationSender: NullGameNotificationService(),
            randomDouble: { 0 },
            drawDelayNanoseconds: 0,
            initialLanguage: .english,
            persistState: false
        )
        store.library = [
            makeGame(appId: 10, title: "Rendered Game", installState: .available),
            makeGame(appId: 20, title: "Rendered Tool", installState: .installed, productCategory: .tool)
        ]
        store.preferences.filters.includedCategories = [.game, .tool]
        store.selectedGame = store.library[0]

        let view = NSHostingView(
            rootView: ContentView()
                .environmentObject(store)
                .frame(width: 900, height: 650)
        )
        view.frame = NSRect(x: 0, y: 0, width: 900, height: 650)
        view.appearance = NSAppearance(named: .darkAqua)
        view.layoutSubtreeIfNeeded()

        guard let representation = view.bitmapImageRepForCachingDisplay(in: view.bounds) else {
            XCTFail("Expected a bitmap representation for the rendered ContentView.")
            return
        }

        view.cacheDisplay(in: view.bounds, to: representation)

        XCTAssertGreaterThanOrEqual(representation.pixelsWide, 900)
        XCTAssertGreaterThanOrEqual(representation.pixelsHigh, 650)
        XCTAssertGreaterThan(nonBlankSampleCount(in: representation), 50)
    }

    func testContentViewDeclaresAutomationIdentifiersForCoreControls() throws {
        let source = try String(
            contentsOfFile: "src/Presentation/SteamBacklogPicker.Mac/Views/ContentView.swift",
            encoding: .utf8
        )
        let requiredIdentifiers = [
            "LanguageSelector",
            "FiltersPanel",
            "Filters_RequireInstalled",
            "Filters_ExcludeDeckUnsupported",
            "Filters_IncludeGames",
            "Filters_IncludeSoundtracks",
            "Filters_IncludeSoftware",
            "Filters_IncludeTools",
            "Filters_IncludeVideos",
            "Filters_IncludeOther",
            "Filters_IncludeSteam",
            "Filters_SelectCollection",
            "Filters_RefreshButton",
            "Filters_DrawButton",
            "StatusMessage",
            "GameDetailsPanel",
            "GameDetails_SelectedTitle",
            "GameDetails_InstallationStatus",
            "GameDetails_InstallButton",
            "GameDetails_PlayButton",
            "GameDetails_DrawingOverlay"
        ]

        for identifier in requiredIdentifiers {
            XCTAssertTrue(source.contains(".accessibilityIdentifier(\"\(identifier)\")"), "Missing \(identifier)")
        }
    }

    func testContentTypeTogglesBindToMatchingSelectionCategories() throws {
        let source = try String(
            contentsOfFile: "src/Presentation/SteamBacklogPicker.Mac/Views/ContentView.swift",
            encoding: .utf8
        )
        let expectedBindings = [
            #"Toggle(store.text("Filters_IncludeGames"), isOn: store.bindingForCategory(.game))"#,
            #"Toggle(store.text("Filters_IncludeSoundtracks"), isOn: store.bindingForCategory(.soundtrack))"#,
            #"Toggle(store.text("Filters_IncludeSoftware"), isOn: store.bindingForCategory(.software))"#,
            #"Toggle(store.text("Filters_IncludeTools"), isOn: store.bindingForCategory(.tool))"#,
            #"Toggle(store.text("Filters_IncludeVideos"), isOn: store.bindingForCategory(.video))"#,
            #"Toggle(store.text("Filters_IncludeOther"), isOn: store.bindingForCategory(.other))"#
        ]

        for expectedBinding in expectedBindings {
            XCTAssertTrue(source.contains(expectedBinding), "Missing content type binding: \(expectedBinding)")
        }
    }

    func testSteamStorefrontToggleActivatesStorefrontFiltering() throws {
        let source = try String(
            contentsOfFile: "src/Presentation/SteamBacklogPicker.Mac/Views/ContentView.swift",
            encoding: .utf8
        )

        XCTAssertTrue(source.contains("store.preferences.filters.filterByStorefront = true"))
        XCTAssertTrue(source.contains("store.preferences.filters.includedStorefronts.insert(.steam)"))
        XCTAssertTrue(source.contains("store.preferences.filters.includedStorefronts.remove(.steam)"))
    }

    func testCollectionPickerStaysInScrollableFilterContent() throws {
        let source = try String(
            contentsOfFile: "src/Presentation/SteamBacklogPicker.Mac/Views/ContentView.swift",
            encoding: .utf8
        )

        let scrollViewRange = try XCTUnwrap(source.range(of: "ScrollView {"))
        let collectionRange = try XCTUnwrap(source.range(of: "SectionLabel(store.text(\"Filters_CollectionLabel\"))"))
        let scrollContentEndRange = try XCTUnwrap(source.range(of: ".toggleStyle(.checkbox)"))

        XCTAssertLessThan(scrollViewRange.lowerBound, collectionRange.lowerBound)
        XCTAssertLessThan(collectionRange.lowerBound, scrollContentEndRange.lowerBound)
        XCTAssertTrue(source.contains("private struct CollectionMenu: View"))
        XCTAssertTrue(source.contains(".frame(maxWidth: .infinity, minHeight: 34, alignment: .leading)"))
        XCTAssertTrue(source.contains(".accessibilityValue(currentSelection)"))
    }

    func testMacWindowUsesCompactTitlebarAndResizeLimits() throws {
        let source = try String(
            contentsOfFile: "src/Presentation/SteamBacklogPicker.Mac/App/SteamBacklogPickerMacApp.swift",
            encoding: .utf8
        )

        XCTAssertTrue(source.contains("NSWindowDelegate"))
        XCTAssertFalse(source.contains(".fullSizeContentView"))
        XCTAssertTrue(source.contains("window.titlebarAppearsTransparent = false"))
        XCTAssertTrue(source.contains("static let maxSize = NSSize(width: 1168, height: 830)"))
        XCTAssertTrue(source.contains("window.collectionBehavior = [.fullScreenNone]"))
        XCTAssertTrue(source.contains("window.standardWindowButton(.zoomButton)?.isEnabled = false"))
        XCTAssertTrue(source.contains("func windowWillResize(_ sender: NSWindow, to frameSize: NSSize) -> NSSize"))
        XCTAssertTrue(source.contains("clampWindowToAllowedSize(window)"))
    }

    func testEmptySelectionInstallationStatusUsesUnknownStateText() throws {
        let source = try String(
            contentsOfFile: "src/Presentation/SteamBacklogPicker.Mac/Views/ContentView.swift",
            encoding: .utf8
        )

        XCTAssertTrue(source.contains("store.text(\"GameDetails_InstallState_Unknown\")"))
        XCTAssertFalse(source.contains("store.selectedGame.map(store.installStateText) ?? store.text(\"GameDetails_DrawPrompt\")"))
    }

    func testAppMenuCommandsTargetShellAndValidateStoreAvailability() throws {
        let source = try String(
            contentsOfFile: "src/Presentation/SteamBacklogPicker.Mac/App/SteamBacklogPickerMacApp.swift",
            encoding: .utf8
        )

        XCTAssertTrue(source.contains("refreshItem.target = self"))
        XCTAssertTrue(source.contains("drawItem.target = self"))
        XCTAssertTrue(source.contains("return store?.canRefresh == true"))
        XCTAssertTrue(source.contains("return store?.canDraw == true"))
        XCTAssertTrue(source.contains("Task { await store?.refreshLibrary() }"))
        XCTAssertTrue(source.contains("Task { await store?.drawGame() }"))
    }

    private func nonBlankSampleCount(in representation: NSBitmapImageRep) -> Int {
        let width = representation.pixelsWide
        let height = representation.pixelsHigh
        var count = 0

        for x in stride(from: 0, to: width, by: 45) {
            for y in stride(from: 0, to: height, by: 45) {
                guard let color = representation.colorAt(x: x, y: y) else {
                    continue
                }

                if color.alphaComponent > 0.01 &&
                    (color.redComponent > 0.01 || color.greenComponent > 0.01 || color.blueComponent > 0.01) {
                    count += 1
                }
            }
        }

        return count
    }

    private func makeGame(
        appId: UInt32,
        title: String,
        installState: InstallState,
        productCategory: ProductCategory = .game
    ) -> GameEntry {
        GameEntry(
            storefront: .steam,
            steamAppId: appId,
            title: title,
            ownershipType: .owned,
            installState: installState,
            productCategory: productCategory,
            sizeOnDisk: nil,
            lastPlayed: nil,
            tags: ["Rendered"],
            deckCompatibility: .verified,
            coverURL: nil
        )
    }
}
