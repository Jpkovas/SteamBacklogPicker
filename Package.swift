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
    dependencies: [
        .package(url: "https://github.com/facebook/zstd.git", exact: "1.5.7")
    ],
    targets: [
        .executableTarget(
            name: "SteamBacklogPickerMac",
            dependencies: [.product(name: "libzstd", package: "zstd")],
            path: "src/Presentation/SteamBacklogPicker.Mac"
        ),
        .testTarget(
            name: "SteamBacklogPickerMacTests",
            dependencies: ["SteamBacklogPickerMac", .product(name: "libzstd", package: "zstd")],
            path: "tests/Presentation/SteamBacklogPicker.Mac.Tests"
        )
    ]
)
