import SwiftUI
import CoreMotion
import Foundation

enum MotionActivity {
#if BOWLING
    static let bowling = true
    static let title = "Kinesthetic · Bowling Motion"
    static let equipment = "wrist"
    static let game = "bowling"
    static let path = "bowling-motion"
    static let type = "bowling"
    static let healthKey = "bowlingPlayers"
    static let instruction = "Face the pins. Hold your hand still in Bowling."
#else
    static let bowling = false
    static let title = "Kinesthetic · Club Motion"
    static let equipment = "club"
    static let game = "golf"
    static let path = "golf"
    static let type = "club"
    static let healthKey = "players"
    static let instruction = "Calibrate at address in Unity after mounting."
#endif
    static let activeNotification = Notification.Name("org.kinesthetic.motion.active")
}

// Adapted from Aircade's MotionModel.start/receive: real CMHeadphoneMotionManager,
// reporting-earbud identity, finite quaternions and increasing sensor timestamps.
// Native motion requires a supported paired AirPods set; no simulated fallback.
@MainActor final class ClubMotionBridge: ObservableObject {
    @Published var player = "patient"
    // The relay runs on the Mac that runs Unity. A second AirPod pair (e.g. a wrist strap) lives on another Mac,
    // so its app points at that Mac's LAN address and carries the pairing token from local-data/pair-token.txt.
    @Published var relayHost = UserDefaults.standard.string(forKey: "relayHost") ?? "127.0.0.1" {
        didSet { UserDefaults.standard.set(relayHost, forKey: "relayHost") }
    }
    @Published var pairToken = UserDefaults.standard.string(forKey: "pairToken") ?? "" {
        didSet { UserDefaults.standard.set(pairToken, forKey: "pairToken") }
    }
    @Published var status = "Looking for paired AirPods…"
    @Published var source = "Waiting"
    @Published var samples = 0
    @Published var running = false
    @Published var monitoring = false
    @Published var relayConnected = false
    @Published var routeHeld = false
    @Published var speed = 0.0
    private var manager: CMHeadphoneMotionManager?
    private var socket: URLSessionWebSocketTask?
    private let motionQueue = OperationQueue()
    private var session = UUID().uuidString
    private var lastTime = -1.0
    private var sending = false
    private var lastSampleReceived = 0.0
    private var streamStarted = 0.0
    private var discoveryTimer: Timer?
    // Holds the AirPods as the active audio output; without it macOS hands output
    // back to the speakers when a bud leaves the ear and motion silently stops.
    private let route = AudioRouteKeeper(nameMatch: "AirPods")

    func startAutomatically() {
        monitoring = true
        DistributedNotificationCenter.default().postNotificationName(MotionActivity.activeNotification,
            object: MotionActivity.game, userInfo: nil, deliverImmediately: true)
        if discoveryTimer == nil {
            discoveryTimer = Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { [weak self] _ in
                Task { @MainActor in self?.checkConnection() }
            }
        }
        checkConnection()
    }

    private func checkConnection() {
        // Runs on the existing 2s discovery timer; claim() is idempotent.
        if let note = route.claim() { status = note }
        routeHeld = route.holding
        if running {
            if ProcessInfo.processInfo.systemUptime - lastSampleReceived > 5 {
                status = "AirPod motion paused. Reconnecting…"
                stop(keepStatus: true)
            } else if ProcessInfo.processInfo.systemUptime - streamStarted > 1.5,
                      let task = socket {
                Task { await verifyRelay(task) }
            }
            return
        }
        if CMHeadphoneMotionManager().isDeviceMotionAvailable { start() }
        else { status = "Waiting for paired AirPods with motion tracking…" }
    }

    private func relayURL(_ scheme: String, path: String = "", query: [URLQueryItem] = []) -> URL? {
        var parts = URLComponents()
        let host = relayHost.trimmingCharacters(in: .whitespaces)
        parts.scheme = scheme; parts.host = host.isEmpty ? "127.0.0.1" : host; parts.port = 8767; parts.path = "/" + path
        let token = pairToken.trimmingCharacters(in: .whitespacesAndNewlines)
        let items = query + (token.isEmpty ? [] : [URLQueryItem(name: "token", value: token)])
        parts.queryItems = items.isEmpty ? nil : items
        return parts.url
    }

    private func verifyRelay(_ task: URLSessionWebSocketTask) async {
        guard running, socket === task else { return }
        do {
            let (data, _) = try await URLSession.shared.data(from: relayURL("http")!)
            let health = try JSONSerialization.jsonObject(with: data) as? [String: Any]
            let players = health?[MotionActivity.healthKey] as? [String] ?? []
            guard running, socket === task else { return }
            if players.contains(player) && samples > 0 && ProcessInfo.processInfo.systemUptime-lastSampleReceived < 1 {
                relayConnected = true
                status = "Connected to Unity \(MotionActivity.game)."
            }
            else { status = "AirPod motion detected; reconnecting to Unity…"; stop(keepStatus: true) }
        } catch {
            guard running, socket === task else { return }
            status = "Motion relay at \(relayHost) unavailable. Retrying…"
            stop(keepStatus: true)
        }
    }

    func start() {
        stop()
        let manager = CMHeadphoneMotionManager()
        guard manager.isDeviceMotionAvailable else {status="AirPod motion is unavailable. Pair a supported set and try again.";return}
        self.manager=manager
        guard let url=relayURL("ws",path:MotionActivity.path,query:[URLQueryItem(name:"role",value:"producer"),URLQueryItem(name:"player",value:player)]) else {status="Relay address is not valid.";return}
        let task=URLSession.shared.webSocketTask(with:url)
        socket=task;task.resume()
        session=UUID().uuidString;lastTime = -1;samples=0;running=true
        lastSampleReceived=ProcessInfo.processInfo.systemUptime
        streamStarted=lastSampleReceived
        status="Waiting for real AirPod motion and the local relay…"
        motionQueue.maxConcurrentOperationCount=1
        manager.startDeviceMotionUpdates(to:motionQueue) { [weak self] motion,error in
            guard let motion else {
                if let error { Task { @MainActor in self?.status=error.localizedDescription } }
                return
            }
            let q=motion.attitude.quaternion,r=motion.rotationRate
            let sensorSource: String
            switch motion.sensorLocation {
            case .headphoneLeft:sensorSource="Left"
            case .headphoneRight:sensorSource="Right"
            default:sensorSource="Unknown"
            }
            let time=motion.timestamp
            let received=ProcessInfo.processInfo.systemUptime
            Task { @MainActor in
                guard let self, self.running, self.socket === task,
                    ProcessInfo.processInfo.systemUptime-received < 0.25,
                    time > self.lastTime, sensorSource != "Unknown",
                    [q.x,q.y,q.z,q.w,r.x,r.y,r.z,time].allSatisfy({$0.isFinite}) else {return}
                self.lastTime=time;self.source=sensorSource
                self.lastSampleReceived=ProcessInfo.processInfo.systemUptime
                self.speed=sqrt(r.x*r.x+r.y*r.y+r.z*r.z)
                guard !self.sending else {return}
                let p:[String:Any]=["type":"\(MotionActivity.type).motion","playerId":self.player,
                    "sourceId":sensorSource,"sessionId":self.session,"sequence":self.samples,
                    "sensorTime":time,"quaternion":[q.x,q.y,q.z,q.w],"rotationRate":[r.x,r.y,r.z]]
                guard let data=try? JSONSerialization.data(withJSONObject:p),let text=String(data:data,encoding:.utf8) else{return}
                self.samples+=1;self.sending=true
                do {try await task.send(.string(text))}
                catch {self.status="Relay connection paused. Reconnecting…";self.stop(keepStatus:true)}
                self.sending=false
            }
        }
    }
    func stop(keepStatus:Bool=false) {
        running=false;relayConnected=false;source="Waiting";speed=0
        manager?.stopDeviceMotionUpdates();manager=nil
        socket?.cancel(with:.goingAway,reason:nil);socket=nil;sending=false
        if !keepStatus {status="Stopped. Unity will require calibration after reconnecting."}
    }
    func pause() {
        monitoring = false
        discoveryTimer?.invalidate(); discoveryTimer=nil
        route.release()
        stop()
    }
}

final class ClubAppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        DistributedNotificationCenter.default().addObserver(self, selector: #selector(otherActivityStarted(_:)),
            name: MotionActivity.activeNotification, object: nil)
    }
    @objc private func otherActivityStarted(_ notification: Notification) {
        guard let activity = notification.object as? String, activity != MotionActivity.game else { return }
        NotificationCenter.default.post(name: Notification.Name("PauseOtherMotion"), object: nil)
    }
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        NotificationCenter.default.post(name: Notification.Name("ResumeClubMotion"), object: nil)
        sender.activate(ignoringOtherApps: true)
        for window in sender.windows where window.canBecomeMain { window.makeKeyAndOrderFront(nil) }
        return true
    }
}
@main struct KinestheticClubApp: App {
    @NSApplicationDelegateAdaptor(ClubAppDelegate.self) var appDelegate
    @StateObject private var bridge=ClubMotionBridge()
    var body: some Scene {
        WindowGroup(MotionActivity.title) {
            VStack(alignment:.leading,spacing:18) {
                Text(bridge.relayConnected ? "Your \(MotionActivity.equipment). Connected." : bridge.monitoring ? "Connecting your \(MotionActivity.equipment)…" : "\(MotionActivity.game.capitalized) motion paused.").font(.largeTitle.bold())
                Text("AirPods motion → Unity \(MotionActivity.game)").foregroundStyle(.secondary)
                if !MotionActivity.bowling {
                    Picker("Player",selection:$bridge.player) {
                        Text("Patient").tag("patient");Text("Friend").tag("friend")
                    }.disabled(bridge.running)
                }
                // Labelled, because a filled field loses its placeholder and the two are easy to swap.
                Grid(alignment:.leading,horizontalSpacing:10,verticalSpacing:8) {
                    GridRow {
                        Text("Relay Mac IP").foregroundStyle(.secondary)
                        TextField("127.0.0.1 = this Mac",text:$bridge.relayHost)
                    }
                    GridRow {
                        Text("Pairing token").foregroundStyle(.secondary)
                        SecureField("Only when the relay is another Mac",text:$bridge.pairToken)
                    }
                }.textFieldStyle(.roundedBorder).onSubmit { if bridge.monitoring { bridge.start() } }
                Text(bridge.status).fixedSize(horizontal:false,vertical:true)
                Text("Reporting AirPod: \(bridge.source)")
if bridge.routeHeld {Text("Holding the AirPods audio route so motion continues off-ear.").font(.caption).foregroundStyle(.secondary)}
                Text(String(format:"Angular speed: %.2f rad/s · %d samples",bridge.speed,bridge.samples)).monospacedDigit()
                HStack {
                    Button(bridge.monitoring ? "Pause motion" : "Start motion") {if bridge.monitoring {bridge.pause()} else {bridge.startAutomatically()}}
                    Text(MotionActivity.instruction).font(.caption).foregroundStyle(.secondary)
                }
            }.padding(28).frame(width:510)
                .task { bridge.startAutomatically() }
                .onReceive(NotificationCenter.default.publisher(for: Notification.Name("ResumeClubMotion"))) { _ in bridge.startAutomatically() }
                .onReceive(NotificationCenter.default.publisher(for: Notification.Name("PauseOtherMotion"))) { _ in bridge.pause() }
        }.windowResizability(.contentSize)
    }
}
