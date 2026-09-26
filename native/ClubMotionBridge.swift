import SwiftUI
import CoreMotion
import Foundation

// Adapted from Aircade's MotionModel.start/receive: real CMHeadphoneMotionManager,
// reporting-earbud identity, finite quaternions and increasing sensor timestamps.
// Native motion requires a supported paired AirPods set; no simulated fallback.
@MainActor final class ClubMotionBridge: ObservableObject {
    @Published var player = "patient"
    @Published var status = "Looking for paired AirPods…"
    @Published var source = "Waiting"
    @Published var samples = 0
    @Published var running = false
    @Published var relayConnected = false
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

    func startAutomatically() {
        if discoveryTimer == nil {
            discoveryTimer = Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { [weak self] _ in
                Task { @MainActor in self?.checkConnection() }
            }
        }
        checkConnection()
    }

    private func checkConnection() {
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

    private func verifyRelay(_ task: URLSessionWebSocketTask) async {
        guard running, socket === task else { return }
        do {
            let (data, _) = try await URLSession.shared.data(from: URL(string: "http://127.0.0.1:8767/")!)
            let health = try JSONSerialization.jsonObject(with: data) as? [String: Any]
            let players = health?["players"] as? [String] ?? []
            guard running, socket === task else { return }
            if players.contains(player) && samples > 0 && ProcessInfo.processInfo.systemUptime-lastSampleReceived < 1 {
                relayConnected = true
                status = "Streaming to Unity via the local golf relay."
            }
            else { status = "AirPod motion detected; reconnecting to Unity…"; stop(keepStatus: true) }
        } catch {
            guard running, socket === task else { return }
            status = "Local golf relay unavailable. Retrying…"
            stop(keepStatus: true)
        }
    }

    func start() {
        stop()
        let manager = CMHeadphoneMotionManager()
        guard manager.isDeviceMotionAvailable else {status="AirPod motion is unavailable. Pair a supported set and try again.";return}
        self.manager=manager
        let url=URL(string:"ws://127.0.0.1:8767/golf?role=producer&player=\(player)")!
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
                let p:[String:Any]=["type":"club.motion","playerId":self.player,
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
        discoveryTimer?.invalidate(); discoveryTimer=nil
        stop()
    }
}

final class ClubAppDelegate: NSObject, NSApplicationDelegate {
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        NotificationCenter.default.post(name: Notification.Name("ResumeClubMotion"), object: nil)
        return true
    }
}
@main struct KinestheticClubApp: App {
    @NSApplicationDelegateAdaptor(ClubAppDelegate.self) var appDelegate
    @StateObject private var bridge=ClubMotionBridge()
    var body: some Scene {
        WindowGroup("Kinesthetic · Club Motion") {
            VStack(alignment:.leading,spacing:18) {
                Text(bridge.relayConnected ? "Your club. Connected." : "Connecting your club…").font(.largeTitle.bold())
                Text("AirPods motion → Unity golf").foregroundStyle(.secondary)
                Picker("Player",selection:$bridge.player) {
                    Text("Patient").tag("patient");Text("Friend").tag("friend")
                }.disabled(bridge.running)
                Text(bridge.status).fixedSize(horizontal:false,vertical:true)
                Text("Reporting AirPod: \(bridge.source)")
                Text(String(format:"Angular speed: %.2f rad/s · %d samples",bridge.speed,bridge.samples)).monospacedDigit()
                HStack {
                    Button(bridge.running ? "Stop motion" : "Start motion") {if bridge.running {bridge.pause()} else {bridge.startAutomatically()}}
                    Text("Calibrate at address in Unity after mounting.").font(.caption).foregroundStyle(.secondary)
                }
            }.padding(28).frame(width:510)
                .task { bridge.startAutomatically() }
                .onReceive(NotificationCenter.default.publisher(for: Notification.Name("ResumeClubMotion"))) { _ in bridge.startAutomatically() }
        }.windowResizability(.contentSize)
    }
}
