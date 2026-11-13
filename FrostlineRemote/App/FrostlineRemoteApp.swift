import SwiftUI

@main
struct FrostlineRemoteApp: App {
    @StateObject private var connection = RemoteConnection()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(connection)
                .preferredColorScheme(.dark)
        }
    }
}
