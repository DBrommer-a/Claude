// swift-tools-version:5.9
import PackageDescription

// Plattformunabhängiger Kern der App „Frostline Remote“: Funkprotokoll, Stick-Logik,
// Gyro-Umrechnung, Laufzeitmessung. Läuft auch unter Linux: `swift test`.
let package = Package(
    name: "FrostlineRemoteKit",
    platforms: [.iOS(.v17), .macOS(.v13)],
    products: [
        .library(name: "FrostlineRemoteKit", targets: ["FrostlineRemoteKit"]),
    ],
    targets: [
        .target(name: "FrostlineRemoteKit"),
        .testTarget(name: "FrostlineRemoteKitTests", dependencies: ["FrostlineRemoteKit"]),
    ]
)
