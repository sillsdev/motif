"""A stdio connector; the project and server executable never enter this boundary."""
import socket
import sys
import threading


def main():
    connection = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
    connection.connect(sys.argv[1])

    def receive():
        while True:
            data = connection.recv(65536)
            if not data:
                break
            sys.stdout.buffer.write(data)
            sys.stdout.buffer.flush()

    reader = threading.Thread(target=receive)
    reader.start()
    try:
        while True:
            data = sys.stdin.buffer.read1(65536)
            if not data:
                break
            connection.sendall(data)
        connection.shutdown(socket.SHUT_WR)
        reader.join(timeout=10)
    finally:
        if reader.is_alive():
            connection.shutdown(socket.SHUT_RDWR)
        connection.close()
    if reader.is_alive():
        raise RuntimeError("The tool channel did not close")


if __name__ == "__main__":
    main()
