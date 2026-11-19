const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const os = require("node:os");

const root = fs.mkdtempSync(path.join(os.tmpdir(), "workday-editor-"));
const workspace = path.join(root, "files-audit-001-01234567");
const status = path.join(root, ".editor-" + path.basename(workspace));
let report;
const vscode = {
    workspace: {
        workspaceFolders: [{ uri: { scheme: "file", fsPath: workspace } }],
        textDocuments: [],
        onDidChangeTextDocument: callback => { report = callback; }
    },
    window: {
        onDidChangeActiveTextEditor() {},
        onDidChangeTextEditorSelection() {},
        createOutputChannel() {}
    },
    commands: { registerCommand() {} }
};
const sandbox = {
    require: name => name === "vscode" ? vscode : require(name),
    module: { exports: {} },
    process: { pid: process.pid, env: { TOUCH_TYPING_WORKDAY_ROOT: root } }
};

const block = "count = 4\nprint(count)";
const position = offset => ({ offset, isEqual: other => offset === other.offset });
function edit(file, text, start = 0, end = 0, isDirty = false) {
    const selection = { start: position(start), end: position(end) };
    vscode.window.activeTextEditor = {
        selections: [selection],
        selection,
        document: {
            uri: { scheme: "file", fsPath: file },
            isDirty,
            lineCount: text.split("\n").length,
            positionAt: position,
            lineAt: () => ({ range: { end: position(text.length) } }),
            getText: range => range ? text.slice(range.start.offset, range.end.offset) : text
        }
    };
    report();
    return fs.readFileSync(status, "utf8");
}

try {
    const extension = path.join(__dirname, "../code-lesson/lesson.js");
    vm.runInNewContext(fs.readFileSync(extension, "utf8"), sandbox, { filename: extension });
    sandbox.module.exports.activate({ subscriptions: [] });
    const source = path.join(workspace, "a-source.txt");
    const target = path.join(workspace, "c-target.txt");
    for (const eol of ["\n", "\r\n"]) {
        const marked = block.replace("\n", eol);
        const prefix = ["# Черновик", block.split("\n")[0], "# BEGIN TRANSFER", ""].join(eol);
        const original = prefix + marked + eol + "# END TRANSFER" + eol + marked;
        const start = prefix.length, end = start + marked.length;
        assert.equal(edit(source, original, 0, original.length), "a-source.txt\nother");
        assert.equal(edit(source, original, start, end - 1), "a-source.txt\nother");
        assert.equal(edit(source, original, start, end, true), "a-source.txt\nother");
        assert.equal(edit(source, original, original.length - marked.length, original.length), "a-source.txt\nother");
        assert.equal(edit(source, original, start, end), "a-source.txt\nblock");
    }
    assert.equal(edit(path.join(workspace, "..", "a-source.txt"), block, 0, block.length), "-\nother");
    assert.equal(edit(target, "# Черновик\n" + block + "\nextra"), "c-target.txt\nother");
    assert.equal(edit(target, "# Черновик\r\n" + block), "c-target.txt\npasted");
    assert.equal(edit(target, "# Копия\n" + block), "c-target.txt\nrevised");
    vscode.workspace.textDocuments = [{ isDirty: true, uri: { scheme: "file", fsPath: source } }];
    assert.equal(edit(target, "# Копия\n" + block), "c-target.txt\nother");
    vscode.window.activeTextEditor = undefined;
    report();
    assert.equal(fs.readFileSync(status, "utf8"), "-\nother");
    console.log("PASS Workday editor state contract");
} finally {
    fs.rmSync(root, { recursive: true, force: true });
}
