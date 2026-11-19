// VS Code's extension test runner invokes run() in the real extension host.
const assert = require("node:assert/strict");
const { spawn } = require("node:child_process");
const { once } = require("node:events");
const fs = require("node:fs/promises");
const path = require("node:path");
const vscode = require("vscode");

async function exercise() {
    const folder = vscode.workspace.workspaceFolders[0].uri.fsPath;
    const state = path.join(path.dirname(folder), ".editor-" + path.basename(folder));
    const file = path.join(folder, "review-test.txt");
    async function expectState(expected) {
        let actual;
        for (let attempt = 0; attempt < 100; attempt++) {
            try { actual = await fs.readFile(state, "utf8"); }
            catch (error) { if (error.code !== "ENOENT") throw error; }
            if (actual === expected) return;
            await new Promise(resolve => setTimeout(resolve, 100));
        }
        assert.equal(actual, expected);
    }
    async function acceptQuickInput(query) {
        await vscode.commands.executeCommand("workbench.action.quickOpen", query);
        // Quick Open asynchronously populates the result to which Enter is sent.
        await new Promise(resolve => setTimeout(resolve, 500));
        await vscode.commands.executeCommand("workbench.action.acceptSelectedQuickOpenItem");
    }

    // Automatic workspace activation is part of this contract.
    await expectState("-\nother");
    await acceptQuickInput(file);
    await expectState("review-test.txt\nother");
    const editor = vscode.window.activeTextEditor;
    const doc = editor.document;
    await acceptQuickInput(":2");
    await expectState("review-test.txt\nstart");
    editor.selection = new vscode.Selection(1, 3, 1, 3);
    await expectState("review-test.txt\nother");
    editor.selection = new vscode.Selection(1, 0, 1, 0);
    await expectState("review-test.txt\nstart");
    editor.selection = new vscode.Selection(1, 0, 1, 5);
    await expectState("review-test.txt\nother");
    editor.selection = new vscode.Selection(1, 0, 1, 0);
    await vscode.commands.executeCommand("cursorEndSelect");
    await expectState("review-test.txt\nselected");
    await vscode.commands.executeCommand("type", { text: "Status: reviewed" });
    await doc.save();
    await expectState("review-test.txt\nother");
    assert.equal((await fs.readFile(file, "utf8")).replace(/\r\n/g, "\n"), "QA / Windows\nStatus: reviewed");

    const copy = await vscode.workspace.openTextDocument(vscode.Uri.file(path.join(folder, "Copies", "review-test.txt")));
    await vscode.window.showTextDocument(copy);
    await expectState("-\nother");
    const script = path.join(folder, "summary.py");
    await acceptQuickInput(script);
    await vscode.commands.executeCommand("cursorTop");
    await expectState("summary.py\nvariable-start");
    await vscode.commands.executeCommand("editor.action.addSelectionToNextFindMatch");
    await expectState("summary.py\nvariable-one");
    // Reproduce a short Windows sharing lock on the trainer's state file. The
    // selection has to reach it after release without another keyboard action.
    const locker = spawn("powershell.exe", ["-NoProfile", "-Command",
        "$f=[IO.File]::Open($env:EDITOR_TEST_STATE,'Open','Read','Read');" +
        "[Console]::WriteLine('held'); try { [void][Console]::ReadLine() } finally { $f.Dispose() }"], {
        windowsHide: true, env: { ...process.env, EDITOR_TEST_STATE: state }
    });
    try {
        const message = await Promise.race([once(locker.stdout, "data"), once(locker, "exit")]);
        assert.ok(String(message[0]).includes("held"), "state file held for the selection");
        await vscode.commands.executeCommand("editor.action.selectHighlights");
        await new Promise(resolve => setTimeout(resolve, 200));
    } finally { locker.stdin.end("\n"); }
    await expectState("summary.py\nvariable-both");
    await vscode.commands.executeCommand("type", { text: "total" });
    await vscode.window.activeTextEditor.document.save();
    await expectState("summary.py\nother");
    assert.equal((await fs.readFile(script, "utf8")).replace(/\r\n/g, "\n"), "total = 3\nprint(total)");
    assert.ok((await vscode.commands.getCommands()).includes("touchTypingStudio.runLesson"));
    await fs.writeFile(path.join(path.dirname(folder), "editor-test-passed"), "ok");
}

exports.run = async () => {
    try { await exercise(); }
    catch (error) {
        await fs.writeFile(path.join(process.env.TOUCH_TYPING_WORKDAY_ROOT, "editor-test-failure"), error.stack || String(error));
        throw error;
    }
};
