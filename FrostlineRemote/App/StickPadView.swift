import SwiftUI
import UIKit
#if canImport(FrostlineRemoteKit)
import FrostlineRemoteKit
#endif

/// Ausgabe der beiden virtuellen Sticks.
struct StickOutput {
    var throttle: Float
    var yaw: Float
    var roll: Float
    var pitch: Float
}

/// Zwei Sticks im Modus 2 mit Multitouch: links Gas (federt nicht zurück) und Gier,
/// rechts Nick und Roll (federn zurück). Ein Stick erscheint dort, wo der Daumen aufsetzt;
/// so liegt er immer bequem, egal wie man das Telefon hält.
struct StickPad: UIViewRepresentable {
    var stickRadius: CGFloat
    var throttleTravel: CGFloat
    var deadband: Float
    var rightStickEnabled: Bool
    var onChange: (StickOutput) -> Void

    func makeUIView(context: Context) -> StickPadUIView {
        let view = StickPadUIView()
        view.onChange = onChange
        return view
    }

    func updateUIView(_ view: StickPadUIView, context: Context) {
        view.stickRadius = stickRadius
        view.throttleTravel = throttleTravel
        view.deadband = deadband
        view.rightStickEnabled = rightStickEnabled
        view.onChange = onChange
    }
}

final class StickPadUIView: UIView {
    var onChange: ((StickOutput) -> Void)?
    var stickRadius: CGFloat = 70 { didSet { setNeedsLayout() } }
    var throttleTravel: CGFloat = 220
    var deadband: Float = 0.02
    var rightStickEnabled = true {
        didSet {
            rightBase.isHidden = !rightStickEnabled
            rightKnob.isHidden = !rightStickEnabled
            if !rightStickEnabled { rightTouch = nil; roll = 0; pitch = 0; emit() }
        }
    }

    private var leftTouch: UITouch?
    private var rightTouch: UITouch?
    private var leftOrigin = CGPoint.zero
    private var rightOrigin = CGPoint.zero
    private var throttle = RelativeThrottle()
    private var yaw: Float = 0
    private var roll: Float = 0
    private var pitch: Float = 0

    private let leftBase = CAShapeLayer()
    private let leftKnob = CAShapeLayer()
    private let rightBase = CAShapeLayer()
    private let rightKnob = CAShapeLayer()
    private let throttleTrack = CAShapeLayer()
    private let throttleFill = CAShapeLayer()

    override init(frame: CGRect) {
        super.init(frame: frame)
        isMultipleTouchEnabled = true
        backgroundColor = .clear
        for layer in [leftBase, rightBase] {
            layer.fillColor = UIColor.white.withAlphaComponent(0.04).cgColor
            layer.strokeColor = UIColor.white.withAlphaComponent(0.22).cgColor
            layer.lineWidth = 2
            self.layer.addSublayer(layer)
        }
        for layer in [leftKnob, rightKnob] {
            layer.fillColor = UIColor.white.withAlphaComponent(0.9).cgColor
            self.layer.addSublayer(layer)
        }
        throttleTrack.fillColor = UIColor.white.withAlphaComponent(0.08).cgColor
        throttleFill.fillColor = UIColor.white.withAlphaComponent(0.7).cgColor
        self.layer.addSublayer(throttleTrack)
        self.layer.addSublayer(throttleFill)
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) wird nicht verwendet")
    }

    override func layoutSubviews() {
        super.layoutSubviews()
        if leftTouch == nil { leftOrigin = CGPoint(x: bounds.width * 0.18, y: bounds.height * 0.62) }
        if rightTouch == nil { rightOrigin = CGPoint(x: bounds.width * 0.82, y: bounds.height * 0.62) }
        redraw()
    }

    // MARK: Berührungen

    override func touchesBegan(_ touches: Set<UITouch>, with event: UIEvent?) {
        for touch in touches {
            let p = touch.location(in: self)
            if p.x < bounds.midX, leftTouch == nil {
                leftTouch = touch
                leftOrigin = p
                throttle.begin(touchY: Double(p.y))
            } else if p.x >= bounds.midX, rightTouch == nil, rightStickEnabled {
                rightTouch = touch
                rightOrigin = p
            }
        }
        update()
    }

    override func touchesMoved(_ touches: Set<UITouch>, with event: UIEvent?) {
        update()
    }

    override func touchesEnded(_ touches: Set<UITouch>, with event: UIEvent?) {
        release(touches)
    }

    override func touchesCancelled(_ touches: Set<UITouch>, with event: UIEvent?) {
        release(touches)
    }

    private func release(_ touches: Set<UITouch>) {
        for touch in touches {
            if touch == leftTouch {
                leftTouch = nil
                yaw = 0 // Gier federt zurück, Gas bleibt stehen
            }
            if touch == rightTouch {
                rightTouch = nil
                roll = 0
                pitch = 0
            }
        }
        update()
    }

    private func update() {
        if let touch = leftTouch {
            let p = touch.location(in: self)
            let axes = StickMath.springAxes(dx: Double(p.x - leftOrigin.x), dy: 0, radius: Double(stickRadius))
            yaw = StickMath.deadband(axes.x, deadband)
            throttle.move(touchY: Double(p.y), travel: Double(throttleTravel))
        }
        if let touch = rightTouch {
            let p = touch.location(in: self)
            let axes = StickMath.springAxes(dx: Double(p.x - rightOrigin.x), dy: Double(p.y - rightOrigin.y), radius: Double(stickRadius))
            roll = StickMath.deadband(axes.x, deadband)
            pitch = StickMath.deadband(axes.y, deadband)
        }
        emit()
        redraw()
    }

    private func emit() {
        onChange?(StickOutput(throttle: throttle.value, yaw: yaw, roll: roll, pitch: pitch))
    }

    // MARK: Zeichnen

    private func redraw() {
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        let r = stickRadius
        leftBase.path = UIBezierPath(ovalIn: CGRect(x: leftOrigin.x - r, y: leftOrigin.y - r, width: r * 2, height: r * 2)).cgPath
        rightBase.path = UIBezierPath(ovalIn: CGRect(x: rightOrigin.x - r, y: rightOrigin.y - r, width: r * 2, height: r * 2)).cgPath

        let knob = r * 0.38
        let leftKnobCenter = CGPoint(x: leftOrigin.x + CGFloat(yaw) * r, y: leftOrigin.y)
        leftKnob.path = UIBezierPath(ovalIn: CGRect(x: leftKnobCenter.x - knob, y: leftKnobCenter.y - knob, width: knob * 2, height: knob * 2)).cgPath
        let rightKnobCenter = CGPoint(x: rightOrigin.x + CGFloat(roll) * r, y: rightOrigin.y - CGFloat(pitch) * r)
        rightKnob.path = UIBezierPath(ovalIn: CGRect(x: rightKnobCenter.x - knob, y: rightKnobCenter.y - knob, width: knob * 2, height: knob * 2)).cgPath

        // Gasanzeige neben dem linken Stick.
        let track = CGRect(x: leftOrigin.x - r - 26, y: leftOrigin.y - r, width: 10, height: r * 2)
        throttleTrack.path = UIBezierPath(roundedRect: track, cornerRadius: 5).cgPath
        let fillHeight = track.height * CGFloat(throttle.value)
        throttleFill.path = UIBezierPath(roundedRect: CGRect(x: track.minX, y: track.maxY - fillHeight, width: track.width, height: fillHeight), cornerRadius: 5).cgPath
        CATransaction.commit()
    }
}
