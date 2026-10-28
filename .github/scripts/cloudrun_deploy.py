#!/usr/bin/env python3
"""Puts the website (web-site/, made by build.sh WebServer) on the member's own Google Cloud Run.

It runs as AppMare's own service account (appmare-deployer), which the member gave "Cloud Run Admin"
and "Service Account User" on their project; no Google sign-in of the member is used. The image goes
to AppMare's Artifact Registry; the orchestrator already let the member project's Cloud Run pull it.

Python stdlib only; JWT signing with the openssl command. Environment:
  APPMARE_GCP_DEPLOYER_KEY   the service account's JSON key (repository secret of the build repo)
  APPMARE_IMAGE_REPOSITORY   e.g. us-central1-docker.pkg.dev/appmare-a4a18/sites
  GCP_PROJECT_ID, GCP_REGION, CLOUD_RUN_SERVICE
  APPMARE_APP_ID, APPMARE_MEMBER_ID, APPMARE_LICENSE_BINDING, APPMARE_SITE_TOKEN, APPMARE_APP_TITLE

Commands:
  token           print an access token for docker login
  image           print the image name for this run
  deploy IMAGE    create or update the service with IMAGE, open it to the public, print its address
"""
import base64
import json
import os
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request

PROJECT = os.environ["GCP_PROJECT_ID"]
REGION = os.environ.get("GCP_REGION") or "us-central1"
SERVICE = os.environ["CLOUD_RUN_SERVICE"]
REPOSITORY = os.environ.get("APPMARE_IMAGE_REPOSITORY") or "us-central1-docker.pkg.dev/appmare-a4a18/sites"
SCOPE = "https://www.googleapis.com/auth/cloud-platform"
# Settings the site passes on to MauiWeb (its AppMare license and title).
SITE_ENV = ["APPMARE_APP_ID", "APPMARE_MEMBER_ID", "APPMARE_LICENSE_BINDING", "APPMARE_SITE_TOKEN", "APPMARE_APP_TITLE"]


def fail(message):
    print(f"ERROR: {message}", file=sys.stderr)
    sys.exit(1)


def log(message):
    print(f">>> {message}", file=sys.stderr, flush=True)


def call(url, token=None, method="GET", body=None, form=None):
    data = None
    request = urllib.request.Request(url, method=method)
    if form is not None:
        data = urllib.parse.urlencode(form).encode()
        request.add_header("Content-Type", "application/x-www-form-urlencoded")
    elif body is not None:
        data = json.dumps(body).encode()
        request.add_header("Content-Type", "application/json")
    if token:
        request.add_header("Authorization", f"Bearer {token}")
    try:
        with urllib.request.urlopen(request, data=data, timeout=60) as response:
            text = response.read().decode()
            return response.status, (json.loads(text) if text.strip() else {})
    except urllib.error.HTTPError as e:
        text = e.read().decode()
        try:
            return e.code, json.loads(text)
        except ValueError:
            return e.code, {"error": {"message": text}}


def b64url(data):
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode()


def access_token():
    """The service account's token: a JWT signed with its key, exchanged at Google."""
    raw = os.environ.get("APPMARE_GCP_DEPLOYER_KEY")
    if not raw:
        fail("AppMare's Google Cloud publishing account is not configured (APPMARE_GCP_DEPLOYER_KEY).")
    key = json.loads(raw)
    token_uri = key.get("token_uri", "https://oauth2.googleapis.com/token")
    now = int(time.time())
    header = b64url(json.dumps({"alg": "RS256", "typ": "JWT"}).encode())
    claims = b64url(json.dumps({"iss": key["client_email"], "scope": SCOPE, "aud": token_uri, "iat": now, "exp": now + 3600}).encode())
    with tempfile.NamedTemporaryFile("w", delete=False) as pem:
        pem.write(key["private_key"])
        pem_path = pem.name
    try:
        os.chmod(pem_path, 0o600)
        signature = subprocess.run(["openssl", "dgst", "-sha256", "-sign", pem_path],
                                   input=f"{header}.{claims}".encode(), capture_output=True, check=True).stdout
    finally:
        os.remove(pem_path)
    status, answer = call(token_uri, method="POST", form={
        "grant_type": "urn:ietf:params:oauth:grant-type:jwt-bearer",
        "assertion": f"{header}.{claims}.{b64url(signature)}",
    })
    if status != 200 or "access_token" not in answer:
        fail(f"AppMare's Google Cloud account was refused ({answer.get('error', status)}).")
    return answer["access_token"]


def wait(token, operation, base):
    """Waits for a long-running Cloud Run operation."""
    name = operation.get("name")
    for _ in range(120):
        if operation.get("done"):
            if "error" in operation:
                fail(operation["error"].get("message", "operation failed"))
            return operation
        time.sleep(5)
        status, operation = call(f"{base}/{name}", token)
        if status != 200:
            fail(f"could not follow {name} ({status})")
    fail(f"{name} did not finish in 10 minutes")


def image_name():
    tag = f"{os.environ.get('GITHUB_RUN_ID', 'local')}-{os.environ.get('GITHUB_RUN_ATTEMPT', '1')}"
    return f"{REPOSITORY}/{SERVICE}:{tag}"


def deploy(token, image):
    service_url = f"https://run.googleapis.com/v2/projects/{PROJECT}/locations/{REGION}/services/{SERVICE}"
    env = [{"name": name, "value": os.environ[name]} for name in SITE_ENV if os.environ.get(name)]
    body = {
        "ingress": "INGRESS_TRAFFIC_ALL",
        "labels": {"created-by": "appmare"},
        "template": {
            "containers": [{
                "image": image,
                "ports": [{"containerPort": 8080}],
                "env": env,
                # One process per visitor: room for about 20 at a time per instance.
                "resources": {"limits": {"cpu": "1", "memory": "2Gi"}, "cpuIdle": True},
            }],
            # Visitors stay connected (WebSocket) and should come back to the same instance.
            "timeout": "3600s",
            "sessionAffinity": True,
            "maxInstanceRequestConcurrency": 80,
            "executionEnvironment": "EXECUTION_ENVIRONMENT_GEN2",
            # A cap, so a traffic spike can't run up the member's bill.
            "scaling": {"minInstanceCount": 0, "maxInstanceCount": 2},
        },
    }
    status, op = call(f"{service_url}?allowMissing=true", token, "PATCH", body)
    if status == 403:
        fail("AppMare has no access to your Google Cloud project any more. Connect Google Cloud again in AppMare "
             "(Cloud Run Admin and Service Account User for AppMare's publishing account).")
    if status != 200:
        fail(f"Cloud Run refused the service ({op.get('error', {}).get('message', status)})")
    log("Deploying to Cloud Run…")
    wait(token, op, "https://run.googleapis.com/v2")

    status, answer = call(f"{service_url}:setIamPolicy", token, "POST",
                          {"policy": {"bindings": [{"role": "roles/run.invoker", "members": ["allUsers"]}]}})
    if status != 200:
        fail("the site is deployed but could not be opened to the public "
             f"({answer.get('error', {}).get('message', status)}). Organization accounts can block this "
             "(policy constraints/iam.allowedPolicyMemberDomains).")

    status, service = call(service_url, token)
    if status != 200 or not service.get("uri"):
        fail("could not read the site's address")
    return service["uri"]


def main():
    command = sys.argv[1] if len(sys.argv) > 1 else ""
    if command == "image":
        print(image_name())
        return
    token = access_token()
    if command == "token":
        print(token)
    elif command == "deploy" and len(sys.argv) > 2:
        print(deploy(token, sys.argv[2]))
    else:
        fail(__doc__)


if __name__ == "__main__":
    main()
