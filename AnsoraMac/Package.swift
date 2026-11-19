// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "AnsoraMac",
    platforms: [.macOS(.v13)],
    products: [.executable(name: "AnsoraAI", targets: ["AnsoraMac"])],
    targets: [
        .target(name: "AnsoraCore"),
        .executableTarget(name: "AnsoraMac", dependencies: ["AnsoraCore"]),
        .testTarget(name: "AnsoraCoreTests", dependencies: ["AnsoraCore"])
    ]
)
