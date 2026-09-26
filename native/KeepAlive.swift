import Foundation
import CoreAudio
import AVFoundation

/// Holds the AirPods as the active audio output so CoreMotion keeps delivering.
///
/// `CMHeadphoneMotionManager` only reports while the AirPods are the system's
/// active output device. Taking a bud out of the ear — or simply letting the link
/// idle — hands output back to the built-in speakers and the motion stream stops
/// with no error: samples just stop arriving. Measured on a stationary desk setup,
/// that produced 8 interruptions in 175 seconds, including gaps of 89s, 11s and
/// 3.8s. Each multi-second reconnect also costs 20-33 degrees of false rotation
/// while Apple's fusion filter re-converges, which is enough to fire a phantom
/// swing or poison a club calibration.
///
/// Two things are needed to read motion with a bud out of the ear. This class is
/// the half that can be automated:
///
///   1. Claim the AirPods as the default output device, re-claiming periodically
///      so a reconnect is recaptured without restarting the app.
///   2. Play a continuous inaudible tone so the route does not idle out when
///      nothing else is making sound.
///
/// The other half is **Automatic Ear Detection**, which must be turned off in
/// System Settings > Bluetooth > AirPods. That setting lives on the device and is
/// not scriptable. With it on and a bud out of the ear, macOS stops publishing the
/// AirPods as an audio device at all, so there is nothing here to claim.
final class AudioRouteKeeper {

    private let nameMatch: String
    private let engine = AVAudioEngine()
    private var toneRunning = false
    private var phase: Double = 0
    private var heldDeviceID: AudioDeviceID = 0

    /// Amplitude of the holding tone. -80 dBFS: inaudible in practice, but not
    /// digital silence, which some devices treat as idle and disconnect.
    private let amplitude: Double = 0.0001
    private let toneHz: Double = 220

    /// True while this process holds the route and the tone is running.
    private(set) var holding = false

    init(nameMatch: String = "AirPods") {
        self.nameMatch = nameMatch
    }

    // ---- CoreAudio device plumbing ------------------------------------------

    private static func systemObject() -> AudioObjectID {
        AudioObjectID(kAudioObjectSystemObject)
    }

    /// Devices that actually have output streams, with their names.
    private static func outputDevices() -> [(id: AudioDeviceID, name: String)] {
        var addr = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDevices,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain)
        var size: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(systemObject(), &addr, 0, nil, &size) == noErr,
              size > 0 else { return [] }

        let count = Int(size) / MemoryLayout<AudioDeviceID>.size
        var ids = [AudioDeviceID](repeating: 0, count: count)
        guard AudioObjectGetPropertyData(systemObject(), &addr, 0, nil, &size, &ids) == noErr
        else { return [] }

        var result: [(AudioDeviceID, String)] = []
        for id in ids {
            // Skip input-only devices.
            var streams = AudioObjectPropertyAddress(
                mSelector: kAudioDevicePropertyStreams,
                mScope: kAudioDevicePropertyScopeOutput,
                mElement: kAudioObjectPropertyElementMain)
            var streamSize: UInt32 = 0
            guard AudioObjectGetPropertyDataSize(id, &streams, 0, nil, &streamSize) == noErr,
                  streamSize > 0 else { continue }

            var nameAddr = AudioObjectPropertyAddress(
                mSelector: kAudioObjectPropertyName,
                mScope: kAudioObjectPropertyScopeGlobal,
                mElement: kAudioObjectPropertyElementMain)
            var cfName: CFString? = nil
            var nameSize = UInt32(MemoryLayout<CFString?>.size)
            guard withUnsafeMutablePointer(to: &cfName, { ptr -> Bool in
                AudioObjectGetPropertyData(id, &nameAddr, 0, nil, &nameSize, ptr) == noErr
            }), let name = cfName else { continue }

            result.append((id, name as String))
        }
        return result
    }

    private static func defaultOutputDevice() -> AudioDeviceID {
        var addr = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDefaultOutputDevice,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain)
        var id: AudioDeviceID = 0
        var size = UInt32(MemoryLayout<AudioDeviceID>.size)
        AudioObjectGetPropertyData(systemObject(), &addr, 0, nil, &size, &id)
        return id
    }

    @discardableResult
    private static func setDefaultOutputDevice(_ id: AudioDeviceID) -> Bool {
        var device = id
        var addr = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDefaultOutputDevice,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain)
        return AudioObjectSetPropertyData(
            systemObject(), &addr, 0, nil,
            UInt32(MemoryLayout<AudioDeviceID>.size), &device) == noErr
    }

    // ---- the holding tone ----------------------------------------------------

    private func startTone() {
        guard !toneRunning else { return }
        let output = engine.outputNode
        let format = output.inputFormat(forBus: 0)
        guard format.channelCount > 0, format.sampleRate > 0 else { return }

        let rate = format.sampleRate
        let source = AVAudioSourceNode { [weak self] _, _, frameCount, audioBufferList in
            guard let self else { return noErr }
            let buffers = UnsafeMutableAudioBufferListPointer(audioBufferList)
            let step = 2.0 * Double.pi * self.toneHz / rate
            for frame in 0..<Int(frameCount) {
                let value = Float(sin(self.phase) * self.amplitude)
                self.phase += step
                if self.phase > 2.0 * Double.pi { self.phase -= 2.0 * Double.pi }
                for buffer in buffers {
                    guard let data = buffer.mData?.assumingMemoryBound(to: Float.self) else { continue }
                    data[frame] = value
                }
            }
            return noErr
        }

        engine.attach(source)
        engine.connect(source, to: engine.mainMixerNode, format: format)
        do {
            try engine.start()
            toneRunning = true
        } catch {
            toneRunning = false
        }
    }

    private func restartTone() {
        if toneRunning { engine.stop(); toneRunning = false }
        // The engine binds to whatever device was default when it started, so it
        // has to be rebuilt after the route changes.
        engine.reset()
        startTone()
    }

    // ---- public API ----------------------------------------------------------

    /// Claims the route if it is not already held. Idempotent and cheap, so it can
    /// be called from an existing polling timer rather than owning one.
    /// Returns a status line only when something changed, otherwise nil.
    @discardableResult
    func claim() -> String? {
        guard let target = AudioRouteKeeper.outputDevices()
                .first(where: { $0.name.localizedCaseInsensitiveContains(nameMatch) })
        else {
            if holding || heldDeviceID != 0 {
                heldDeviceID = 0
                holding = false
                if toneRunning { engine.stop(); toneRunning = false }
                return "\(nameMatch) left the audio device list. Turn off Automatic Ear "
                     + "Detection in System Settings > Bluetooth > AirPods to keep motion "
                     + "running off-ear."
            }
            return nil
        }

        let isNewDevice = target.id != heldDeviceID
        heldDeviceID = target.id

        if AudioRouteKeeper.defaultOutputDevice() != target.id {
            if AudioRouteKeeper.setDefaultOutputDevice(target.id) {
                restartTone()
                holding = toneRunning
                return "Holding \(target.name) as the audio output so motion continues off-ear."
            }
            holding = false
            return "Could not claim \(target.name) as the audio output."
        }

        if isNewDevice || !toneRunning {
            restartTone()
            holding = toneRunning
            return isNewDevice ? "Holding \(target.name) as the audio output." : nil
        }

        holding = toneRunning
        return nil
    }

    /// Stops the tone and gives the audio route back to the user.
    func release() {
        if toneRunning { engine.stop(); toneRunning = false }
        holding = false
        heldDeviceID = 0
    }
}
