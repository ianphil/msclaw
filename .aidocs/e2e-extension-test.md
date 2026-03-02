# E2E Extension Test (Manual Smoke Test)

This verifies all five extension capability paths in MsClaw using the sample extension at:

`/home/cip/src/ernist/extensions/hello-world`

## 0) Build sample extension (if needed)

```bash
cd /home/cip/src/ernist/extensions/hello-world/src/HelloWorld.Extension
dotnet build -c Release -v q
mkdir -p /home/cip/src/ernist/extensions/hello-world/runtime
cp -f bin/Release/net9.0/HelloWorld.Extension.dll /home/cip/src/ernist/extensions/hello-world/runtime/
cp -f bin/Release/net9.0/HelloWorld.Extension.pdb /home/cip/src/ernist/extensions/hello-world/runtime/
```

Manifest should exist at:

`/home/cip/src/ernist/extensions/hello-world/plugin.json`

## 1) Start MsClaw with the test mind

```bash
cd /home/cip/src/msclaw
dotnet run --project src/MsClaw -- --mind /home/cip/src/ernist --urls http://127.0.0.1:5061
```

Keep this process running in terminal A.

## 2) Validate extension load + HTTP route

In terminal B:

```bash
BASE=http://127.0.0.1:5061

# extension loader visibility
curl -s "$BASE/extensions"

# extension HTTP route path
curl -s "$BASE/ext/hello-world"
```

Expected:
- `/extensions` includes `hello-world` with `"tier":1`
- `/ext/hello-world` returns `message: "hello from extension route"`

## 3) Validate command path

```bash
curl -s -X POST "$BASE/command" \
  -H 'Content-Type: application/json' \
  -d '{"message":"/hello-world smoke"}'
```

Expected:
- response contains `hello-world command ok`

## 4) Validate hook path (session + messages)

```bash
# triggers session:create hook
curl -s -X POST "$BASE/session/new"

# triggers message:received and message:sent hooks
curl -s -X POST "$BASE/chat" \
  -H 'Content-Type: application/json' \
  -d '{"message":"Please respond with just: hi"}'

# read hook counters
curl -s -X POST "$BASE/command" \
  -H 'Content-Type: application/json' \
  -d '{"message":"/hello-world counters"}'
```

Expected:
- `sessionCreate` increments after `/session/new`
- `messageReceived` and `messageSent` increment after `/chat`

## 5) Validate tool path

```bash
curl -s -X POST "$BASE/chat" \
  -H 'Content-Type: application/json' \
  -d '{"message":"Use the hello world extension status tool and return only the resulting JSON."}'
```

Expected:
- assistant response includes JSON with `message: "hello from extension tool"`

## 6) Validate service path + warm reload

```bash
# warm reload external extensions
curl -s -X POST "$BASE/command" \
  -H 'Content-Type: application/json' \
  -d '{"message":"/reload"}'

# confirm extension still loaded
curl -s "$BASE/extensions"
```

Expected:
- `/reload` returns `External extensions reloaded.`
- `hello-world` remains loaded and started

## Notes

- If `5050` is already in use, keep using `--urls http://127.0.0.1:5061`.
- Current behavior after `/reload`: route delegates may reference pre-reload instance state, while command/hook/tool counters reflect the reloaded instance.
