import { PublicClientApplication, LogLevel } from "@azure/msal-node";

const config = {
  auth: {
    clientId: "d3ede85c-4af4-43aa-b80a-478f16ccb859",
    authority: "https://login.microsoftonline.com/72f988bf-86f1-41af-91ab-2d7cd011db47",
  },
};

const pca = new PublicClientApplication(config);
const scopes = ["api://d3ede85c-4af4-43aa-b80a-478f16ccb859/access_as_user"];

try {
  console.log("\nOpening browser for authentication...\n");

  const result = await pca.acquireTokenInteractive({
    scopes,
    openBrowser: async (url) => {
      const { exec } = await import("child_process");
      exec(`start "" "${url}"`);
    },
    successTemplate: "<h2>Authentication successful!</h2><p>You can close this tab.</p>",
    errorTemplate: "<h2>Authentication failed</h2><p>{{error}}</p>",
  });

  console.log("\n--- Token Info ---");
  console.log(`Account: ${result.account?.username}`);
  console.log(`Expires: ${result.expiresOn}`);
  console.log(`Scopes:  ${result.scopes.join(", ")}`);

  // If --test flag, call the OpenResponses endpoint
  if (process.argv.includes("--test")) {
    const token = result.accessToken;
    const url = "http://127.0.0.1:18789/v1/responses";
    console.log(`\n--- Testing ${url} ---\n`);
    const res = await fetch(url, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${token}`,
      },
      body: JSON.stringify({ model: "test", input: "hello", stream: false }),
    });
    console.log(`Status: ${res.status} ${res.statusText}`);
    const body = await res.text();
    console.log(body.substring(0, 500));
  } else {
    console.log("\n--- Access Token ---\n");
    console.log(result.accessToken);
    console.log("\nTip: use --test to also call /v1/responses");
  }
} catch (err) {
  console.error("Error:", err.message);
  process.exit(1);
}
