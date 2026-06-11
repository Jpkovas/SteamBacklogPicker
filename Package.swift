// swift-tools-version: 5.9

import PackageDescription

let package = Package(
    name: "SteamBacklogPickerMac",
    platforms: [
        .macOS(.v13)
    ],
    products: [
        .executable(
            name: "SteamBacklogPickerMac",
            targets: ["SteamBacklogPickerMac"]
        )
    ],
    targets: [
        .executableTarget(
            name: "SteamBacklogPickerMac",
            path: "src/Presentation/SteamBacklogPicker.Mac"
        ),
        .testTarget(
            name: "SteamBacklogPickerMacTests",
            dependencies: ["SteamBacklogPickerMac"],
            path: "tests/Presentation/SteamBacklogPicker.Mac.Tests"
        )
    ]
)
