import XCTest
@testable import SteamBacklogPickerMac

final class VDFParserTests: XCTestCase {
    func testParseSupportsEscapedStringsAndComments() throws {
        let content = #"""
        // comment
        "AppState"
        {
            "appid" "480"
            "name" "Spacewar \"Test\""
            "UserConfig"
            {
                "LastPlayed" "1710000000"
            }
        }
        """#

        let root = try VDFParser().parse(content)
        let appState = try XCTUnwrap(root.child("AppState"))

        XCTAssertEqual(appState.child("appid")?.value, "480")
        XCTAssertEqual(appState.child("name")?.value, "Spacewar \"Test\"")
        XCTAssertEqual(appState.path("UserConfig", "LastPlayed")?.value, "1710000000")
    }

    func testParseLibraryFoldersObjectNotation() throws {
        let content = #"""
        "LibraryFolders"
        {
            "0"
            {
                "path" "/Users/test/Library/Application Support/Steam"
            }
            "1"
            {
                "contentpath" "/Volumes/Games/SteamLibrary"
            }
        }
        """#

        let root = try VDFParser().parse(content)
        let libraryFolders = try XCTUnwrap(root.child("LibraryFolders"))

        XCTAssertEqual(libraryFolders.child("0")?.child("path")?.value, "/Users/test/Library/Application Support/Steam")
        XCTAssertEqual(libraryFolders.child("1")?.child("contentpath")?.value, "/Volumes/Games/SteamLibrary")
    }
}
