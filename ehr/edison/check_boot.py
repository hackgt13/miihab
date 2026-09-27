"""
Connects to Intel Edison on COM5, logs in as root, checks boot status.
"""
import serial
import time

PORT = "COM5"
BAUD = 115200

def send(s, cmd, wait=1.0):
    s.write((cmd + "\n").encode())
    time.sleep(wait)
    return read_all(s)

def read_all(s):
    out = b""
    while s.in_waiting:
        out += s.read(s.in_waiting)
        time.sleep(0.1)
    return out.decode(errors="replace")

def main():
    print(f"Opening {PORT} at {BAUD}...")
    with serial.Serial(PORT, BAUD, timeout=2) as s:
        time.sleep(0.5)

        # Wake console
        s.write(b"\n")
        time.sleep(1)
        resp = read_all(s)
        print("=== Initial response ===")
        print(repr(resp))

        # If login prompt, send root
        if "login:" in resp:
            print(">> Sending 'root'")
            resp = send(s, "root", wait=1.5)
            print(repr(resp))

        # If password prompt (Edison default has none)
        if "Password" in resp:
            print(">> Sending empty password")
            resp = send(s, "", wait=1)
            print(repr(resp))

        # Check if we're in
        print(">> Sending uname -a")
        resp = send(s, "uname -a", wait=1)
        print(repr(resp))

        print(">> Checking i2c for Grove LCD")
        resp = send(s, "i2cdetect -y -r 1", wait=2)
        print(repr(resp))

        print(">> Done")

if __name__ == "__main__":
    main()
