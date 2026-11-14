import SwiftUI
import UIKit
#if canImport(FrostlineRemoteKit)
import FrostlineRemoteKit
#endif

/// Was ein Knüppel steuert (Mode 1 oder Mode 2).
struct GimbalRole: Equatable {
    var horizontalName: String
    var verticalName: String
    /// Senkrecht ist Gas: bleibt stehen, Anzeige 0–100 %, Rasterung am Rand.
    var verticalIsThrottle: Bool
}

/// Ein fester Steuerknüppel wie am echten Sender: quadratisches Feld, Fadenkreuz, Skala,
/// griffiger Daumenknopf. Der Daumen darf überall im Feld aufsetzen.
struct GimbalStickView: UIViewRepresentable {
    var role: GimbalRole
    var grip: GimbalStick.Grip
    /// Anteil des sichtbaren Wegs, den der Finger für Vollausschlag braucht (kleiner = empfindlicher).
    var fingerTravel: CGFloat
    var deadband: Float
    var detents: Bool
    var enabled: Bool
    var disabledLabel: String?
    /// x −1…1, y −1…1 oder bei Gas 0…1.
    var onChange: (Float, Float) -> Void
    var onDetent: () -> Void

    func makeUIView(context: Context) -> GimbalUIView {
        let view = GimbalUIView()
        update(view)
        return view
    }

    func updateUIView(_ view: GimbalUIView, context: Context) {
        update(view)
    }

    private func update(_ view: GimbalUIView) {
        view.onChange = onChange
        view.onDetent = onDetent
        view.configure(role: role, grip: grip, fingerTravel: fingerTravel, deadband: deadband,
                       detents: detents, enabled: enabled, disabledLabel: disabledLabel)
    }
}

final class GimbalUIView: UIView {
    var onChange: ((Float, Float) -> Void)?
    var onDetent: (() -> Void)?

    private var role = GimbalRole(horizontalName: "", verticalName: "", verticalIsThrottle: false)
    private var stick = GimbalStick()
    private var fingerTravel: CGFloat = 0.85
    private var deadband: Float = 0.02
    private var detentsOn = true
    private var enabled = true
    private var touch: UITouch?
    private var detentX = DetentTracker(detents: [0])
    private var detentY = DetentTracker(detents: [0])

    // Angezeigte Knüppelstellung; läuft beim Zurückfedern weich hinterher.
    private var shownX: CGFloat = 0
    private var shownY: CGFloat = 0
    private var displayLink: CADisplayLink?

    private let plate = CAShapeLayer()
    private let glow = CAGradientLayer()
    private let glowMask = CAShapeLayer()
    private let grid = CAShapeLayer()
    private let ticks = CAShapeLayer()
    private let boot = CAShapeLayer()
    private let shaft = CAShapeLayer()
    private let knob = CAShapeLayer()
    private let knobGrip = CAShapeLayer()
    private let knobDot = CAShapeLayer()
    private let verticalLabel = CATextLayer()
    private let horizontalLabel = CATextLayer()
    private let valueLabel = CATextLayer()
    private let offLabel = CATextLayer()

    override init(frame: CGRect) {
        super.init(frame: frame)
        isMultipleTouchEnabled = false
        backgroundColor = .clear

        plate.fillColor = UIColor(white: 1, alpha: 0.035).cgColor
        plate.strokeColor = UIColor(white: 1, alpha: 0.14).cgColor
        plate.lineWidth = 1
        layer.addSublayer(plate)

        // Weicher Lichthof in der Mitte, wie eine beleuchtete Knüppelmanschette.
        glow.type = .radial
        glow.colors = [UIColor(white: 1, alpha: 0.10).cgColor, UIColor(white: 1, alpha: 0).cgColor]
        glow.startPoint = CGPoint(x: 0.5, y: 0.5)
        glow.endPoint = CGPoint(x: 1, y: 1)
        glow.mask = glowMask
        layer.addSublayer(glow)

        grid.strokeColor = UIColor(white: 1, alpha: 0.10).cgColor
        grid.fillColor = UIColor.clear.cgColor
        grid.lineWidth = 1
        grid.lineDashPattern = [3, 5]
        layer.addSublayer(grid)

        ticks.strokeColor = UIColor(white: 1, alpha: 0.28).cgColor
        ticks.lineWidth = 1.5
        ticks.lineCap = .round
        layer.addSublayer(ticks)

        boot.fillColor = UIColor(white: 0, alpha: 0.35).cgColor
        boot.strokeColor = UIColor(white: 1, alpha: 0.16).cgColor
        boot.lineWidth = 1
        layer.addSublayer(boot)

        shaft.strokeColor = UIColor(white: 0.55, alpha: 1).cgColor
        shaft.lineCap = .round
        layer.addSublayer(shaft)

        knob.fillColor = UIColor(white: 0.93, alpha: 1).cgColor
        knob.shadowColor = UIColor.black.cgColor
        knob.shadowOpacity = 0.6
        knob.shadowRadius = 6
        knob.shadowOffset = CGSize(width: 0, height: 3)
        layer.addSublayer(knob)

        knobGrip.fillColor = UIColor.clear.cgColor
        knobGrip.strokeColor = UIColor(white: 0, alpha: 0.16).cgColor
        knobGrip.lineWidth = 1.2
        layer.addSublayer(knobGrip)

        knobDot.fillColor = UIColor(white: 0, alpha: 0.55).cgColor
        layer.addSublayer(knobDot)

        for label in [verticalLabel, horizontalLabel, valueLabel, offLabel] {
            label.contentsScale = UIScreen.main.scale
            label.foregroundColor = UIColor(white: 1, alpha: 0.55).cgColor
            label.font = UIFont.systemFont(ofSize: 10, weight: .semibold)
            label.fontSize = 10
            layer.addSublayer(label)
        }
        valueLabel.alignmentMode = .center
        valueLabel.font = UIFont.monospacedDigitSystemFont(ofSize: 11, weight: .semibold)
        valueLabel.fontSize = 11
        valueLabel.foregroundColor = UIColor(white: 1, alpha: 0.8).cgColor
        horizontalLabel.alignmentMode = .right
        offLabel.alignmentMode = .center
        offLabel.fontSize = 15
        offLabel.foregroundColor = UIColor(white: 1, alpha: 0.75).cgColor
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) wird nicht verwendet")
    }

    func configure(role newRole: GimbalRole, grip: GimbalStick.Grip, fingerTravel: CGFloat, deadband: Float,
                   detents: Bool, enabled newEnabled: Bool, disabledLabel: String?) {
        if newRole != role {
            role = newRole
            stick = GimbalStick(springX: .center, springY: newRole.verticalIsThrottle ? .hold : .center, grip: grip)
            detentY = DetentTracker(detents: newRole.verticalIsThrottle ? [-1, 0] : [0])
            shownX = CGFloat(stick.x)
            shownY = CGFloat(stick.y)
            verticalLabel.string = newRole.verticalName
            horizontalLabel.string = newRole.horizontalName
            setNeedsLayout()
            emit()
        }
        stick.grip = grip
        self.fingerTravel = max(0.3, min(1.2, fingerTravel))
        self.deadband = deadband
        detentsOn = detents
        if newEnabled != enabled {
            enabled = newEnabled
            if !enabled {
                touch = nil
                stick.release()
                emit()
                animateBack()
            }
        }
        offLabel.string = enabled ? nil : (disabledLabel ?? "AUS")
        alpha = enabled ? 1 : 0.45
        redraw()
    }

    // MARK: Geometrie

    /// Höhe der Wertezeile über dem Feld (außerhalb, damit der Knopf sie nie verdeckt).
    static let labelHeight: CGFloat = 20
    private var side: CGFloat { max(1, min(bounds.width, bounds.height - Self.labelHeight)) }
    private var mid: CGPoint { CGPoint(x: bounds.midX, y: bounds.maxY - side / 2) }
    private var knobRadius: CGFloat { side * 0.15 }
    /// Sichtbarer Weg von der Mitte bis zum Anschlag.
    private var visualTravel: CGFloat { max(1, side * 0.5 - knobRadius - 8) }

    override func layoutSubviews() {
        super.layoutSubviews()
        let rect = CGRect(x: mid.x - side / 2, y: mid.y - side / 2, width: side, height: side)
        let corner = side * 0.16
        plate.path = UIBezierPath(roundedRect: rect, cornerRadius: corner).cgPath
        glow.frame = rect
        glowMask.path = UIBezierPath(roundedRect: CGRect(origin: .zero, size: rect.size), cornerRadius: corner).cgPath

        let t = visualTravel
        let gridPath = UIBezierPath()
        gridPath.move(to: CGPoint(x: mid.x - t, y: mid.y)); gridPath.addLine(to: CGPoint(x: mid.x + t, y: mid.y))
        gridPath.move(to: CGPoint(x: mid.x, y: mid.y - t)); gridPath.addLine(to: CGPoint(x: mid.x, y: mid.y + t))
        gridPath.append(UIBezierPath(rect: CGRect(x: mid.x - t, y: mid.y - t, width: t * 2, height: t * 2)))
        grid.path = gridPath.cgPath

        // Skala: Striche bei 25/50/75/100 % auf beiden Achsen; beim Gas eine Rasterleiste am Rand.
        let tickPath = UIBezierPath()
        for i in -4...4 where i != 0 {
            let f = CGFloat(i) / 4 * t
            let long: CGFloat = abs(i) % 2 == 0 ? 7 : 4
            tickPath.move(to: CGPoint(x: mid.x + f, y: mid.y - long / 2)); tickPath.addLine(to: CGPoint(x: mid.x + f, y: mid.y + long / 2))
            if !role.verticalIsThrottle {
                tickPath.move(to: CGPoint(x: mid.x - long / 2, y: mid.y + f)); tickPath.addLine(to: CGPoint(x: mid.x + long / 2, y: mid.y + f))
            }
        }
        if role.verticalIsThrottle {
            let x = mid.x - t - 2
            for i in 0...20 {
                let y = mid.y + t - CGFloat(i) / 20 * t * 2
                let long: CGFloat = i % 5 == 0 ? 9 : 5
                tickPath.move(to: CGPoint(x: x, y: y)); tickPath.addLine(to: CGPoint(x: x + long, y: y))
            }
        }
        ticks.path = tickPath.cgPath

        let bootRadius = side * 0.22
        boot.path = UIBezierPath(ovalIn: CGRect(x: mid.x - bootRadius, y: mid.y - bootRadius, width: bootRadius * 2, height: bootRadius * 2)).cgPath
        shaft.lineWidth = max(5, side * 0.045)

        let pad = side * 0.06
        // Achsnamen klein in den unteren Ecken, Werte über dem Feld.
        verticalLabel.frame = CGRect(x: rect.minX + pad, y: rect.maxY - pad - 11, width: rect.width / 2, height: 14)
        horizontalLabel.frame = CGRect(x: rect.midX, y: rect.maxY - pad - 11, width: rect.width / 2 - pad, height: 14)
        valueLabel.frame = CGRect(x: rect.minX, y: rect.minY - Self.labelHeight + 2, width: rect.width, height: 16)
        offLabel.frame = CGRect(x: rect.minX, y: rect.minY + rect.height * 0.18, width: rect.width, height: 20)
        redraw()
    }

    // MARK: Berührungen

    override func touchesBegan(_ touches: Set<UITouch>, with event: UIEvent?) {
        guard enabled, touch == nil, let first = touches.first else { return }
        touch = first
        stopAnimation()
        let p = first.location(in: self)
        stick.begin(touchX: Double(p.x), touchY: Double(p.y), centerX: Double(mid.x), centerY: Double(mid.y),
                    travel: Double(visualTravel * fingerTravel))
        changed()
    }

    override func touchesMoved(_ touches: Set<UITouch>, with event: UIEvent?) {
        guard let touch, touches.contains(touch) else { return }
        let p = touch.location(in: self)
        stick.move(touchX: Double(p.x), touchY: Double(p.y), centerX: Double(mid.x), centerY: Double(mid.y),
                   travel: Double(visualTravel * fingerTravel))
        changed()
    }

    override func touchesEnded(_ touches: Set<UITouch>, with event: UIEvent?) { end(touches) }
    override func touchesCancelled(_ touches: Set<UITouch>, with event: UIEvent?) { end(touches) }

    private func end(_ touches: Set<UITouch>) {
        guard let touch, touches.contains(touch) else { return }
        self.touch = nil
        stick.release()
        emit()
        detentX.reset()
        animateBack()
    }

    private func changed() {
        if detentsOn {
            if detentX.crossed(stick.x) { onDetent?() }
            if detentY.crossed(stick.y) { onDetent?() }
        }
        shownX = CGFloat(stick.x)
        shownY = CGFloat(stick.y)
        emit()
        redraw()
    }

    private func emit() {
        let x = StickMath.deadband(stick.x, deadband)
        let y = role.verticalIsThrottle ? GimbalStick.throttle(fromAxis: stick.y) : StickMath.deadband(stick.y, deadband)
        onChange?(x, y)
    }

    // MARK: Zurückfedern

    private func animateBack() {
        guard displayLink == nil else { return }
        let link = CADisplayLink(target: self, selector: #selector(step))
        link.add(to: .main, forMode: .common)
        displayLink = link
    }

    private func stopAnimation() {
        displayLink?.invalidate()
        displayLink = nil
    }

    @objc private func step(_ link: CADisplayLink) {
        let k = min(1, CGFloat(link.targetTimestamp - link.timestamp) * 22)
        shownX += (CGFloat(stick.x) - shownX) * k
        shownY += (CGFloat(stick.y) - shownY) * k
        if abs(shownX - CGFloat(stick.x)) < 0.002 && abs(shownY - CGFloat(stick.y)) < 0.002 {
            shownX = CGFloat(stick.x)
            shownY = CGFloat(stick.y)
            stopAnimation()
        }
        redraw()
    }

    // MARK: Zeichnen

    private func redraw() {
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        let t = visualTravel
        let k = CGPoint(x: mid.x + shownX * t, y: mid.y - shownY * t)

        let shaftPath = UIBezierPath()
        shaftPath.move(to: mid)
        shaftPath.addLine(to: k)
        shaft.path = shaftPath.cgPath

        let r = knobRadius
        knob.path = UIBezierPath(ovalIn: CGRect(x: k.x - r, y: k.y - r, width: r * 2, height: r * 2)).cgPath
        knob.shadowPath = knob.path
        let rings = UIBezierPath()
        for f in [0.78, 0.56] as [CGFloat] {
            rings.append(UIBezierPath(ovalIn: CGRect(x: k.x - r * f, y: k.y - r * f, width: r * f * 2, height: r * f * 2)))
        }
        knobGrip.path = rings.cgPath
        let dot = r * 0.14
        knobDot.path = UIBezierPath(ovalIn: CGRect(x: k.x - dot, y: k.y - dot, width: dot * 2, height: dot * 2)).cgPath

        let x = Int((StickMath.deadband(stick.x, deadband) * 100).rounded())
        if role.verticalIsThrottle {
            let gas = Int((GimbalStick.throttle(fromAxis: stick.y) * 100).rounded())
            valueLabel.string = "\(role.verticalName) \(gas) %   \(role.horizontalName) \(x)"
        } else {
            let y = Int((StickMath.deadband(stick.y, deadband) * 100).rounded())
            valueLabel.string = "\(role.verticalName) \(y)   \(role.horizontalName) \(x)"
        }
        CATransaction.commit()
    }
}
