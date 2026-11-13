import UIKit
#if canImport(FrostlineRemoteKit)
import FrostlineRemoteKit
#endif

/// Vibration bei Scharfschalten, Absturz, Boden- und Gate-Kontakt.
final class Haptics {
    private let light = UIImpactFeedbackGenerator(style: .light)
    private let medium = UIImpactFeedbackGenerator(style: .medium)
    private let heavy = UIImpactFeedbackGenerator(style: .heavy)
    private let rigid = UIImpactFeedbackGenerator(style: .rigid)
    var enabled = true

    init() {
        [light, medium, heavy, rigid].forEach { $0.prepare() }
    }

    func play(_ events: [EventTracker.Event]) {
        guard enabled else { return }
        for event in events {
            switch event {
            case .armed: medium.impactOccurred(intensity: 0.8)
            case .crash: heavy.impactOccurred(intensity: 1.0)
            case .ground: light.impactOccurred(intensity: 0.6)
            case .gate: rigid.impactOccurred(intensity: 0.7)
            }
        }
    }

    func tap() {
        guard enabled else { return }
        light.impactOccurred(intensity: 0.4)
    }
}
