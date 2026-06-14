from __future__ import annotations

import contextlib
import dataclasses
import subprocess
import time
import uuid
from collections.abc import Iterator
from pathlib import Path

from verify_helpers import run_checked


@dataclasses.dataclass(frozen=True)
class PublishedContainer:
    name: str
    host: str
    port: int


@contextlib.contextmanager
def run_published_container(
    *,
    cwd: Path,
    env: dict[str, str],
    name_prefix: str,
    image: str,
    container_port: int,
    host_port: int | None = None,
    platform: str | None = None,
    container_env: dict[str, str] | None = None,
    command: list[str] | None = None,
    timeout_seconds: int = 90,
) -> Iterator[PublishedContainer]:
    name = f"qyl-{name_prefix}-{uuid.uuid4().hex[:12]}"
    run_checked(["docker", "info"], cwd, env)
    publish = f"127.0.0.1:{host_port}:{container_port}" if host_port is not None else f"127.0.0.1::{container_port}"
    command_line = [
        "docker",
        "run",
        "--rm",
        "--detach",
        "--name",
        name,
    ]
    if platform is not None:
        command_line.extend(["--platform", platform])
    for key, value in (container_env or {}).items():
        command_line.extend(["--env", f"{key}={value}"])
    command_line.extend(
        [
            "--publish",
            publish,
            image,
        ]
    )
    if command:
        command_line.extend(command)

    run_checked(
        command_line,
        cwd,
        env,
    )

    try:
        yield _wait_for_published_port(cwd, env, name, container_port, timeout_seconds)
    finally:
        subprocess.run(
            ["docker", "rm", "--force", name],
            cwd=cwd,
            env=env,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
        )


def _wait_for_published_port(
    cwd: Path,
    env: dict[str, str],
    name: str,
    container_port: int,
    timeout_seconds: int,
) -> PublishedContainer:
    deadline = time.monotonic() + timeout_seconds
    port_name = f"{container_port}/tcp"

    while time.monotonic() < deadline:
        completed = subprocess.run(
            ["docker", "port", name, port_name],
            cwd=cwd,
            env=env,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
        )
        if completed.returncode == 0 and completed.stdout.strip():
            host, port = _parse_docker_port(completed.stdout)
            return PublishedContainer(name, host, port)

        time.sleep(0.25)

    raise SystemExit(f"container {name} did not publish {port_name} within {timeout_seconds}s")


def _parse_docker_port(output: str) -> tuple[str, int]:
    first = output.strip().splitlines()[0]
    host, _, port_text = first.rpartition(":")
    if not host or not port_text:
        raise SystemExit(f"unexpected docker port output: {output!r}")

    return host.removeprefix("[").removesuffix("]"), int(port_text)
