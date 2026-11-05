#!/usr/bin/env python3
"""
Ejecuta un comando y guarda TODO lo que imprime (stdout y stderr) en un .txt,
junto con datos del sistema, la hora y el código de salida.

Uso:
    python tools/run_and_log.py -- <comando> [argumentos...]

Ejemplos:
    python tools/run_and_log.py -- msbuild /p:Configuration=Release
    python tools/run_and_log.py -- python mi_script.py --algo

El log se guarda en la carpeta logs/ con un nombre como:
    logs/20261104-230000_msbuild.txt

Si el comando falla, el script termina con el mismo código de salida
y te indica la ruta del archivo para que lo mandes.
"""

import argparse
import datetime
import os
import platform
import subprocess
import sys


def build_header(command, started):
    lines = [
        "=" * 70,
        "LOG DE EJECUCIÓN",
        "=" * 70,
        f"Fecha y hora : {started.isoformat(sep=' ', timespec='seconds')}",
        f"Comando      : {' '.join(command)}",
        f"Carpeta      : {os.getcwd()}",
        f"Sistema      : {platform.system()} {platform.release()} ({platform.version()})",
        f"Arquitectura : {platform.machine()}",
        f"Python       : {platform.python_version()}",
        "=" * 70,
        "",
    ]
    return "\n".join(lines) + "\n"


def main():
    parser = argparse.ArgumentParser(
        description="Ejecuta un comando y guarda su salida en un .txt."
    )
    parser.add_argument(
        "--logdir", default="logs", help="Carpeta donde guardar el log (por defecto: logs)"
    )
    parser.add_argument("command", nargs=argparse.REMAINDER, help="Comando a ejecutar, después de --")
    args = parser.parse_args()

    command = args.command
    if command and command[0] == "--":
        command = command[1:]
    if not command:
        parser.error("Falta el comando a ejecutar. Ejemplo: -- msbuild /p:Configuration=Release")

    started = datetime.datetime.now()
    os.makedirs(args.logdir, exist_ok=True)

    base_name = os.path.basename(command[0]).split(".")[0] or "comando"
    log_name = f"{started.strftime('%Y%m%d-%H%M%S')}_{base_name}.txt"
    log_path = os.path.abspath(os.path.join(args.logdir, log_name))

    print(f"Ejecutando: {' '.join(command)}")
    print(f"Log en    : {log_path}")

    with open(log_path, "w", encoding="utf-8", errors="replace") as log:
        log.write(build_header(command, started))
        log.flush()

        try:
            process = subprocess.Popen(
                command,
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,  # stderr va junto con stdout para no perder el orden
                text=True,
                encoding="utf-8",
                errors="replace",
                bufsize=1,
            )
        except FileNotFoundError:
            msg = f"No se encontró el programa: {command[0]}\n"
            log.write(msg)
            print(msg, file=sys.stderr)
            print(f"Log guardado en: {log_path}")
            return 127

        for line in process.stdout:
            sys.stdout.write(line)
            log.write(line)
        process.wait()

        finished = datetime.datetime.now()
        footer = (
            "\n" + "=" * 70 + "\n"
            f"Terminó       : {finished.isoformat(sep=' ', timespec='seconds')}\n"
            f"Duración      : {(finished - started).total_seconds():.1f} s\n"
            f"Código de salida: {process.returncode}\n"
            + "=" * 70 + "\n"
        )
        log.write(footer)

    print(footer)
    print(f"Log guardado en: {log_path}")
    if process.returncode != 0:
        print("El comando falló. Mandá este archivo para revisarlo.")
    return process.returncode


if __name__ == "__main__":
    sys.exit(main())
