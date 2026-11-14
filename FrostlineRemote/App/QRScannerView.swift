import SwiftUI
import VisionKit
#if canImport(FrostlineRemoteKit)
import FrostlineRemoteKit
#endif

/// Kamera-Scanner für den Kopplungs-QR aus dem Spiel (F1 › Spiel). Nimmt nur frostline://pair-Codes an.
/// Alternativ geht auch die Kamera-App: Der Code öffnet Frostline Remote direkt.
struct QRScannerSheet: View {
    let onPairing: (URL) -> Void
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            Group {
                if DataScannerViewController.isSupported && DataScannerViewController.isAvailable {
                    QRScannerView(onPairing: onPairing)
                        .ignoresSafeArea()
                        .overlay(alignment: .bottom) {
                            Text("QR-Code im Spiel unter F1 › Spiel anvisieren")
                                .font(.callout.weight(.semibold))
                                .padding(.horizontal, 16)
                                .padding(.vertical, 10)
                                .background(.black.opacity(0.6), in: Capsule())
                                .padding(.bottom, 24)
                        }
                } else {
                    VStack(spacing: 12) {
                        Image(systemName: "qrcode.viewfinder").font(.system(size: 48))
                        Text("Der Scanner ist auf diesem Gerät nicht verfügbar (oder die Kamera ist nicht freigegeben).")
                        Text("Einfach den QR-Code mit der Kamera-App scannen – er öffnet Frostline Remote und koppelt sofort.")
                            .foregroundStyle(.secondary)
                    }
                    .multilineTextAlignment(.center)
                    .padding(32)
                }
            }
            .navigationTitle("Mit Mac koppeln")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Abbrechen") { dismiss() } }
            }
        }
    }
}

private struct QRScannerView: UIViewControllerRepresentable {
    let onPairing: (URL) -> Void

    func makeUIViewController(context: Context) -> DataScannerViewController {
        let scanner = DataScannerViewController(recognizedDataTypes: [.barcode(symbologies: [.qr])],
                                                qualityLevel: .balanced,
                                                isHighlightingEnabled: true)
        scanner.delegate = context.coordinator
        try? scanner.startScanning()
        return scanner
    }

    func updateUIViewController(_ controller: DataScannerViewController, context: Context) {}

    func makeCoordinator() -> Coordinator { Coordinator(onPairing: onPairing) }

    final class Coordinator: NSObject, DataScannerViewControllerDelegate {
        let onPairing: (URL) -> Void
        private var done = false

        init(onPairing: @escaping (URL) -> Void) { self.onPairing = onPairing }

        func dataScanner(_ scanner: DataScannerViewController, didAdd items: [RecognizedItem], allItems: [RecognizedItem]) {
            guard !done else { return }
            for item in items {
                if case .barcode(let code) = item, let text = code.payloadStringValue,
                   let url = URL(string: text), RelayPairing(url: url) != nil {
                    done = true
                    scanner.stopScanning()
                    onPairing(url)
                    return
                }
            }
        }
    }
}
