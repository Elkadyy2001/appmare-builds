#!/usr/bin/env python3
"""Puts the website (web-site/, made by build.sh WebServer) on the member's own Azure App Service.

Python stdlib only. Environment (from the publish's GitHub environment):
  AZURE_REFRESH_TOKEN, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, AZURE_TENANT_ID
  AZURE_SUBSCRIPTION_ID, AZURE_LOCATION, AZURE_RESOURCE_GROUP, AZURE_PLAN, AZURE_WEBAPP
  APPMARE_APP_ID, APPMARE_MEMBER_ID, APPMARE_LICENSE_BINDING, APPMARE_SITE_TOKEN, APPMARE_APP_TITLE

Usage: azure_deploy.py deploy DIR    create or update the web app, upload DIR, print the site's address

The site runs on App Service's Linux .NET image (Debian 12): the binary must be built against
glibc 2.36 or older, which build.sh does (Ubuntu 22.04).
"""
import io
import json
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile

ARM = "https://management.azure.com"
SCOPE = "https://management.azure.com/user_impersonation offline_access"
SITE_ENV = ["APPMARE_APP_ID", "APPMARE_MEMBER_ID", "APPMARE_LICENSE_BINDING", "APPMARE_SITE_TOKEN", "APPMARE_APP_TITLE"]
# Runs the copy in /tmp: wwwroot loses the executable bit, and the site writes its license cache.
STARTUP = """#!/bin/sh
rm -rf /tmp/site && cp -R /home/site/wwwroot /tmp/site
chmod +x /tmp/site/site
cd /tmp/site
exec ./site
"""


def env(name, default=None):
    value = os.environ.get(name) or default
    if value is None:
        fail(f"{name} is not set")
    return value


def fail(message):
    print(f"ERROR: {message}", file=sys.stderr)
    sys.exit(1)


def log(message):
    print(f">>> {message}", file=sys.stderr, flush=True)


def call(url, token=None, method="GET", body=None, form=None, data=None, content_type=None, timeout=120):
    """(status, json, headers); errors come back as status and Azure's error body."""
    request = urllib.request.Request(url, method=method)
    if form is not None:
        data = urllib.parse.urlencode(form).encode()
        content_type = "application/x-www-form-urlencoded"
    elif body is not None:
        data = json.dumps(body).encode()
        content_type = "application/json"
    if content_type:
        request.add_header("Content-Type", content_type)
    if token:
        request.add_header("Authorization", f"Bearer {token}")
    try:
        with urllib.request.urlopen(request, data=data, timeout=timeout) as response:
            text = response.read().decode(errors="replace")
            return response.status, parse(text), response.headers
    except urllib.error.HTTPError as e:
        return e.code, parse(e.read().decode(errors="replace")), e.headers


def parse(text):
    try:
        return json.loads(text) if text.strip() else {}
    except ValueError:
        return {"error": {"message": text[:500]}}


def message(answer, status):
    error = answer.get("error") if isinstance(answer, dict) else None
    if isinstance(error, dict):
        return error.get("message") or error.get("code") or str(status)
    return answer.get("error_description") or str(error or status)


def access_token():
    status, answer, _ = call(
        f"https://login.microsoftonline.com/{urllib.parse.quote(env('AZURE_TENANT_ID'))}/oauth2/v2.0/token",
        method="POST",
        form={
            "grant_type": "refresh_token",
            "client_id": env("AZURE_CLIENT_ID"),
            "client_secret": env("AZURE_CLIENT_SECRET"),
            "refresh_token": env("AZURE_REFRESH_TOKEN"),
            "scope": SCOPE,
        })
    if status != 200 or "access_token" not in answer:
        fail(f"the Microsoft sign-in could not be used ({message(answer, status)}). Connect Azure again in AppMare.")
    return answer["access_token"]


def wait_arm(token, status, answer, headers, what):
    """Follows an ARM long-running operation (202 + Azure-AsyncOperation or Location)."""
    if status not in (201, 202):
        return answer
    poll = headers.get("Azure-AsyncOperation") or headers.get("Location")
    if not poll:
        return answer
    for _ in range(120):
        time.sleep(int(headers.get("Retry-After") or 5))
        status, result, headers = call(poll, token)
        if status == 202:
            continue
        state = result.get("status") if isinstance(result, dict) else None
        if state in ("InProgress", "Running", "Accepted", "Creating", "Updating"):
            continue
        if state in ("Failed", "Canceled") or status >= 400:
            fail(f"{what} failed ({message(result, status)})")
        return result
    fail(f"{what} did not finish in 10 minutes")


def put(token, url, body, what):
    status, answer, headers = call(url, token, "PUT", body)
    if status >= 400:
        text = message(answer, status)
        if "quota" in text.lower():
            fail(f"{what}: your Azure subscription has no App Service quota in this region ({text}). "
                 "Request quota in the Azure portal or publish to another region.")
        fail(f"{what} failed ({text})")
    return wait_arm(token, status, answer, headers, what) or answer


def deploy(token, folder):
    sub = f"{ARM}/subscriptions/{env('AZURE_SUBSCRIPTION_ID')}"
    group, plan, name = env("AZURE_RESOURCE_GROUP"), env("AZURE_PLAN"), env("AZURE_WEBAPP")
    location = env("AZURE_LOCATION", "eastus")
    rg = f"{sub}/resourceGroups/{group}"

    # An existing group keeps its region, and everything in it goes there too.
    status, existing, _ = call(f"{rg}?api-version=2021-04-01", token)
    if status == 200:
        location = existing.get("location", location)
    else:
        put(token, f"{rg}?api-version=2021-04-01", {"location": location, "tags": {"createdBy": "AppMare"}}, "Creating the resource group")
        log(f"Resource group {group} created in {location}")

    farm = f"{rg}/providers/Microsoft.Web/serverfarms/{plan}"
    status, existing, _ = call(f"{farm}?api-version=2023-12-01", token)
    if status == 200:
        location = existing.get("location", location)
    else:
        # B1: the smallest plan with Always On and enough memory for a process per visitor.
        put(token, f"{farm}?api-version=2023-12-01", {
            "location": location,
            "kind": "linux",
            "sku": {"name": "B1", "tier": "Basic"},
            "properties": {"reserved": True},
            "tags": {"createdBy": "AppMare"},
        }, "Creating the App Service plan")
        log(f"App Service plan {plan} (B1, Linux) created")

    settings = [{"name": n, "value": os.environ[n]} for n in SITE_ENV if os.environ.get(n)]
    settings += [
        {"name": "PORT", "value": "8080"},
        {"name": "WEBSITES_PORT", "value": "8080"},
        {"name": "MAX_SESSIONS", "value": "10"},
        {"name": "SCM_DO_BUILD_DURING_DEPLOYMENT", "value": "false"},
        {"name": "ENABLE_ORYX_BUILD", "value": "false"},
    ]
    site_url = f"{rg}/providers/Microsoft.Web/sites/{name}"
    site = put(token, f"{site_url}?api-version=2023-12-01", {
        "location": location,
        "kind": "app,linux",
        "tags": {"createdBy": "AppMare"},
        "properties": {
            "serverFarmId": farm.replace(ARM, ""),
            "httpsOnly": True,
            # Visitors keep a WebSocket to their own process: they must come back to the same instance.
            "clientAffinityEnabled": True,
            "siteConfig": {
                # The image only supplies the OS libraries; the site is a native binary.
                "linuxFxVersion": "DOTNETCORE|10.0",
                "appCommandLine": "sh /home/site/wwwroot/startup.sh",
                "webSocketsEnabled": True,
                "alwaysOn": True,
                "http20Enabled": True,
                "ftpsState": "Disabled",
                "minTlsVersion": "1.2",
                "appSettings": settings,
            },
        },
    }, "Creating the web app")
    status, site, _ = call(f"{site_url}?api-version=2023-12-01", token)
    if status != 200:
        fail(f"could not read the web app ({message(site, status)})")
    props = site.get("properties", {})
    host = props.get("defaultHostName") or f"{name}.azurewebsites.net"
    scm = next((s["name"] for s in props.get("hostNameSslStates", []) if s.get("hostType") == "Repository"),
               host.replace(".azurewebsites.net", ".scm.azurewebsites.net", 1))
    log(f"Web app {name} ready")

    package = io.BytesIO()
    with zipfile.ZipFile(package, "w", zipfile.ZIP_DEFLATED) as z:
        for root, _, files in os.walk(folder):
            for file in files:
                path = os.path.join(root, file)
                arcname = os.path.relpath(path, folder)
                if arcname == "Dockerfile":
                    continue
                z.write(path, arcname)
        info = zipfile.ZipInfo("startup.sh")
        info.external_attr = 0o755 << 16
        z.writestr(info, STARTUP)
    size = package.tell()
    log(f"Uploading {size // (1024 * 1024)} MB to App Service…")

    status, answer, headers = call(f"https://{scm}/api/zipdeploy?isAsync=true", token, "POST",
                                   data=package.getvalue(), content_type="application/zip", timeout=600)
    if status not in (200, 202):
        fail(f"App Service refused the upload ({message(answer, status)})")
    poll = headers.get("Location")
    for _ in range(120):
        if not poll:
            break
        time.sleep(5)
        status, deployment, _ = call(poll, token)
        if status == 202 or (status == 200 and not deployment.get("complete")):
            continue
        if status != 200 or deployment.get("status") != 4:
            fail(f"App Service could not install the site ({deployment.get('status_text') or message(deployment, status)})")
        break
    log("Site uploaded, starting…")

    # The first start pulls the image and copies the site: give it a few minutes.
    url = f"https://{host}"
    for _ in range(60):
        try:
            with urllib.request.urlopen(url, timeout=30) as response:
                if response.status < 500:
                    break
        except urllib.error.HTTPError as e:
            if e.code < 500:
                break
        except Exception:
            pass
        time.sleep(5)
    else:
        log("The site didn't answer yet; App Service may still be starting it.")
    return url


def main():
    if len(sys.argv) != 3 or sys.argv[1] != "deploy":
        fail(__doc__)
    if not os.path.isfile(os.path.join(sys.argv[2], "site")):
        fail(f"{sys.argv[2]}/site is missing: run build.sh WebServer first")
    print(deploy(access_token(), sys.argv[2]))


if __name__ == "__main__":
    main()
