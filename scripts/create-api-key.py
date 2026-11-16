#!/usr/bin/env python3
"""Create a 256-bit integration key in .NET development user-secrets."""
import json
from pathlib import Path
import secrets
import subprocess

project = Path(__file__).resolve().parents[1] / "src/CorePlasma.API"
existing = subprocess.run(
    ["dotnet", "user-secrets", "list", "--project", str(project), "--json"],
    capture_output=True, text=True, check=True,
)
# Do not rotate an existing key accidentally or expose unrelated secrets.
payload = existing.stdout[existing.stdout.find("{"):]
stored = json.JSONDecoder().raw_decode(payload)[0] if payload.strip() else {}
if stored.get("ApiKey:Key"):
    print("ApiKey:Key already exists. Use the existing key; no changes made.")
else:
    key = "cp_" + secrets.token_hex(32)
    subprocess.run(
        ["dotnet", "user-secrets", "set", "ApiKey:Key", key, "--project", str(project)],
        capture_output=True, text=True, check=True,
    )
    print("Created API key in development user-secrets:")
    print(key)
