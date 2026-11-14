import SwiftUI

@main
struct FrostlineRemoteApp: App {
    @StateObject private var connection = RemoteConnection()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(connection)
                .preferredColorScheme(.dark)
                // QR-Code aus dem Spiel mit der Kamera-App gescannt: frostline://pair?…
                .onOpenURL { url in connection.pair(with: url) }
        }
    }
}
