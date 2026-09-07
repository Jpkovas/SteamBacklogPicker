import XCTest
@testable import SteamBacklogPickerMac

final class LocalizationTests: XCTestCase {
    func testMacLocalizationResolvesEverySharedDesktopKey() throws {
        let source = try String(
            contentsOfFile: "src/Presentation/SteamBacklogPicker.AppCore/Services/Localization/LocalizationService.cs",
            encoding: .utf8
        )
        let keys = extractSharedLocalizationKeys(from: source)

        XCTAssertGreaterThan(keys.count, 0)
        for key in keys {
            for language in AppLanguage.allCases {
                XCTAssertNotEqual(Localization.text(key, language: language), key, "Missing \(key) for \(language.rawValue)")
            }
        }
    }

    func testSharedDesktopLocalizationKeysResolveForBothLanguages() {
        let keys = [
            "Filters_PanelAutomationName",
            "Filters_SelectCollection",
            "Filters_SelectCollection_HelpText",
            "Filters_RefreshButton_Automation",
            "Filters_DrawButton_Automation",
            "Filters_DrawButton_HelpText",
            "Status_Title",
            "GameDetails_PanelAutomationName",
            "GameDetails_StorefrontLabel",
            "GameDetails_ArtworkLabel",
            "GameDetails_MetadataLabel",
            "GameDetails_SelectedTitleAutomation",
            "GameDetails_InstallationAutomation",
            "GameDetails_PlayButton_Automation",
            "GameDetails_InstallButton_Automation",
            "GameDetails_DrawingOverlay",
            "Storefront_Unknown",
            "Common_GameCount_Singular",
            "Common_GameCount_Plural",
            "GameLaunch_UnsupportedStorefront",
            "GameLaunch_LaunchNotInstalled",
            "GameLaunch_SteamMissingAppId",
            "GameLaunch_SteamAlreadyInstalled",
            "GameLaunch_OpenFailed"
        ]

        for language in AppLanguage.allCases {
            for key in keys {
                XCTAssertNotEqual(Localization.text(key, language: language), key, "Missing \(key) for \(language.rawValue)")
            }
        }
    }

    func testWorkspaceGameCountFormatsNumericArgumentsInBothLanguages() {
        XCTAssertEqual(Localization.format("Workspace_CountSummary", language: .portuguese, 2, 10), "2 de 10 jogos")
        XCTAssertEqual(Localization.format("Workspace_CountSummary", language: .english, 2, 10), "2 of 10 games")
    }

    func testLaunchFailureLocalizationMatchesSharedDesktopStrings() {
        let expected: [(String, AppLanguage, String)] = [
            (
                "GameLaunch_UnsupportedStorefront",
                .portuguese,
                "Esta loja ainda não oferece suporte ao lançamento pelo SteamBacklog Picker."
            ),
            (
                "GameLaunch_UnsupportedStorefront",
                .english,
                "This storefront does not support launching from SteamBacklog Picker yet."
            ),
            (
                "GameLaunch_LaunchNotInstalled",
                .portuguese,
                "Instale o jogo antes de executá-lo."
            ),
            (
                "GameLaunch_LaunchNotInstalled",
                .english,
                "Install the game before launching it."
            ),
            (
                "GameLaunch_SteamMissingAppId",
                .portuguese,
                "O identificador do aplicativo Steam está ausente para este jogo."
            ),
            (
                "GameLaunch_SteamMissingAppId",
                .english,
                "The Steam app identifier is missing for this game."
            ),
            (
                "GameLaunch_SteamAlreadyInstalled",
                .portuguese,
                "O jogo já está instalado via Steam."
            ),
            (
                "GameLaunch_SteamAlreadyInstalled",
                .english,
                "The game is already installed via Steam."
            )
        ]

        for (key, language, value) in expected {
            XCTAssertEqual(Localization.text(key, language: language), value, "\(key) \(language.rawValue)")
        }
    }

    private func extractSharedLocalizationKeys(from source: String) -> Set<String> {
        let pattern = #"\["([^"]+)"\]\s*="#
        let regex = try! NSRegularExpression(pattern: pattern)
        let range = NSRange(source.startIndex..<source.endIndex, in: source)
        return Set(regex.matches(in: source, range: range).compactMap { match in
            guard let keyRange = Range(match.range(at: 1), in: source) else { return nil }
            let key = String(source[keyRange])
            return key.contains("_") ? key : nil
        })
    }
}
