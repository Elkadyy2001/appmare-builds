#!/usr/bin/env python3
"""Deploy wwwroot to Firebase Hosting via the REST API.
Uses only stdlib.  No pip-installed dependencies.

Required environment variables:
  WWWROOT                path to the published static site
  FIREBASE_PROJECT_ID    Firebase project id
  GOOGLE_CLIENT_ID       OAuth 2.0 client id (iOS type — empty secret OK)
  GOOGLE_REFRESH_TOKEN   refresh token issued for the client above
"""

import gzip
import hashlib
import json
import mimetypes
import os
import sys
import urllib.error
import urllib.parse
import urllib.request

TOKEN_URL = "https://oauth2.googleapis.com/token"
HOSTING_API = "https://firebasehosting.googleapis.com/v1beta1"

wwwroot = os.environ["WWWROOT"]
project = os.environ["FIREBASE_PROJECT_ID"]
client_id = os.environ["GOOGLE_CLIENT_ID"]
refresh_token = os.environ["GOOGLE_REFRESH_TOKEN"]


def _request(url, data=None, method="POST", token=None):
    req = urllib.request.Request(url, method=method)
    if token:
        req.add_header("Authorization", f"Bearer {token}")
    if data is not None:
        body = json.dumps(data).encode()
        req.add_header("Content-Type", "application/json; charset=utf-8")
        req.data = body
    try:
        with urllib.request.urlopen(req) as resp:
            return json.loads(resp.read().decode())
    except urllib.error.HTTPError as e:
        body = e.read().decode()
        print(f"HTTP {e.code} on {url}: {body}", file=sys.stderr)
        raise


# ── Step 1: Exchange refresh token for an access token ────────────────────
print(">>> Exchanging refresh token for access token…", flush=True)

token_body = urllib.parse.urlencode({
    "grant_type": "refresh_token",
    "client_id": client_id,
    "client_secret": "",
    "refresh_token": refresh_token,
}).encode()

try:
    req = urllib.request.Request(TOKEN_URL, data=token_body, method="POST")
    with urllib.request.urlopen(req) as resp:
        token_data = json.loads(resp.read().decode())
    access_token = token_data.get("access_token")
    if not access_token:
        err = token_data.get("error", "unknown")
        print(f"ERROR: Google token refresh failed ({err}). Re-authenticate in AppMare app.", file=sys.stderr)
        raise SystemExit(1)
except urllib.error.HTTPError as e:
    body = e.read().decode()
    try:
        err = json.loads(body).get("error", str(e.code))
    except json.JSONDecodeError:
        err = body[:200]
    print(f"ERROR: Google token refresh failed ({err}). Re-authenticate in AppMare app.", file=sys.stderr)
    raise SystemExit(1)

print(">>> Access token obtained", flush=True)

# ── Step 2: Create a new hosting version ─────────────────────────────────
print(">>> Creating hosting version…", flush=True)
version = _request(
    f"{HOSTING_API}/sites/{project}/versions",
    {"config": {"headers": []}},
    token=access_token,
)
version_name = version["name"]
print(f">>> Version: {version_name}", flush=True)

# ── Step 3: Walk wwwroot, gzip each file, hash the gzip'd bytes ────────
# firebase-tools hashes the GZIPPED content, not the original file
print(f">>> Scanning {wwwroot} …", flush=True)
file_hashes = {}       # rel_path → hash of gzip'd content
file_gzipped = {}      # hash → gzip'd bytes (pre-computed for upload)
for root, _dirs, filenames in os.walk(wwwroot):
    for name in filenames:
        full = os.path.join(root, name)
        rel = "/" + os.path.relpath(full, wwwroot)
        with open(full, "rb") as f:
            raw = f.read()
        gzipped = gzip.compress(raw)
        h = hashlib.sha256(gzipped).hexdigest()
        file_hashes[rel] = h
        file_gzipped[h] = gzipped

print(f">>> {len(file_hashes)} files to deploy", flush=True)

# ── Step 4: Populate files (tell Firebase what we have) ────────────────
populated = _request(
    f"{HOSTING_API}/{version_name}:populateFiles",
    {"files": file_hashes},
    token=access_token,
)

upload_url = populated.get("uploadUrl")
required = set(populated.get("uploadRequiredHashes", []))

# ── Step 5: Upload files that Firebase doesn't have yet ────────────────
# Method: POST (not PUT).  Body is raw gzip bytes (hash was of gzip'd bytes).
# NO Content-Encoding header — the gzip'd bytes ARE the content.
if upload_url and required:
    for h in required:
        gzipped = file_gzipped.get(h)
        if gzipped is None:
            continue
        path = next((p for p, ph in file_hashes.items() if ph == h), None)
        content_type, _ = mimetypes.guess_type(path or "")
        print(f">>> Uploading {path} …", flush=True)
        req = urllib.request.Request(
            f"{upload_url}/{h}",
            data=gzipped,
            method="POST",
        )
        req.add_header("Authorization", f"Bearer {access_token}")
        req.add_header("Content-Type", content_type or "application/octet-stream")
        with urllib.request.urlopen(req) as resp:
            if resp.status != 200:
                print(f"Upload failed ({resp.status}): {resp.read().decode()}", file=sys.stderr)
                raise SystemExit(1)

# ── Step 6: Finalize the version ───────────────────────────────────────
print(">>> Finalizing version…", flush=True)
_request(
    f"{HOSTING_API}/{version_name}?updateMask=status",
    {"status": "FINALIZED"},
    method="PATCH",
    token=access_token,
)

# ── Step 7: Create a release ───────────────────────────────────────────
# Use channels/live/releases?versionName={version} (POST body is empty)
print(">>> Creating release…", flush=True)
site_id = project  # project id = default site id
_request(
    f"{HOSTING_API}/projects/-/sites/{site_id}/channels/live/releases?versionName={version_name}",
    token=access_token,
)

url = f"https://{project}.web.app"
print(f">>> ✅ Web app live at {url}", flush=True)
