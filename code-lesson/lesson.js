const { execFile } = require("node:child_process");
const { writeFileSync, renameSync } = require("node:fs");
const fs = require("node:fs/promises");
const path = require("node:path");
const { promisify } = require("node:util");
const vscode = require("vscode");

const execute = promisify(execFile);
const exercises = new Map([
    ["sample.py", "a = 4\nj = 7\nprint(j)\n"],
    ["notes.py", "def triple(value):\n    return value * 3\n\nprint(triple(3))\n"],
    ["draft.py", "def scale(value):\n    return value * 4\n\nprint(scale(4))\n"]
]);

// Only the isolated Workday window receives this root. Report an exercise state
// for its named training files, without collecting editor text from other files.
function watchWorkdayEditor(context) {
    const root = process.env.TOUCH_TYPING_WORKDAY_ROOT;
    const folder = vscode.workspace.workspaceFolders?.[0]?.uri;
    if (!root || !folder || folder.scheme !== "file" || path.relative(root, path.dirname(folder.fsPath)) !== "") return;
    const project = path.basename(folder.fsPath);
    const match = /^(?:editor-|files-)?([a-z]+)-\d{3,}-[0-9a-f]{8}$/.exec(project);
    if (!match) return;
    const name = "review-" + match[1] + ".txt";
    const target = path.join(folder.fsPath, name);
    const script = path.join(folder.fsPath, "summary.py");
    const status = path.join(root, ".editor-" + project);
    const fileLab = project.startsWith("files-");
    const block = "count = 4\nprint(count)";
    const staging = status + "." + process.pid + ".tmp";
    let last;
    let retry;
    const report = () => {
        const editor = vscode.window.activeTextEditor;
        let snapshot = "-\nother";
        const active = editor?.document.uri.scheme === "file" ? editor.document.uri.fsPath : undefined;
        if (fileLab && active && path.relative(folder.fsPath, path.dirname(active)) === "") {
            const filename = path.basename(active);
            const document = editor.document;
            let state = "other";
            if (/^[ac]-source\.txt$/.test(filename) && editor.selections.length === 1 && !document.isDirty) {
                const text = document.getText();
                const excerpt = /^# BEGIN TRANSFER(\r?\n)([^\r\n]+\r?\n[^\r\n]+)\r?\n# END TRANSFER\r?$/gm;
                const match = excerpt.exec(text);
                if (match && !excerpt.exec(text)) {
                    const start = match.index + "# BEGIN TRANSFER".length + match[1].length;
                    const end = start + match[2].length;
                    if (editor.selection.start.isEqual(document.positionAt(start)) &&
                        editor.selection.end.isEqual(document.positionAt(end)) &&
                        document.getText(editor.selection).replace(/\r\n/g, "\n") === block) state = "block";
                }
            }
            if (/^[ac]-target\.txt$/.test(filename)) {
                const value = document.getText().replace(/\r\n/g, "\n");
                const sourceDirty = vscode.workspace.textDocuments.some(candidate => candidate.isDirty &&
                    candidate.uri.scheme === "file" && path.relative(folder.fsPath, path.dirname(candidate.uri.fsPath)) === "" &&
                    /^[ac]-source\.txt$/.test(path.basename(candidate.uri.fsPath)));
                if (value === "# Черновик\n" + block) state = "pasted";
                if (!sourceDirty && value === "# Копия\n" + block) state = "revised";
            }
            if (/^[ac]-(source|target)\.txt$/.test(filename)) snapshot = filename + "\n" + state;
        } else if (active && path.relative(target, active) === "") {
            const selection = editor.selection;
            let state = "other";
            if (editor.selections.length === 1 && editor.document.lineCount >= 2 &&
                selection.start.line === 1 && selection.start.character === 0 && selection.end.line === 1) {
                const line = editor.document.lineAt(1).text;
                if (line === "Status: ready")
                    state = selection.isEmpty ? "start" : selection.end.character === line.length ? "selected" : "other";
            }
            snapshot = name + "\n" + state;
        } else if (active && path.relative(script, active) === "") {
            const ranges = editor.selections;
            const word = (range, line, column) => range.start.line === line && range.end.line === line &&
                range.start.character === column && range.end.character === column + 5 && editor.document.getText(range) === "count";
            let state = "other";
            if (ranges.length === 1 && ranges[0].isEmpty && ranges[0].start.line === 0 && ranges[0].start.character === 0)
                state = "variable-start";
            else if (ranges.length === 1 && word(ranges[0], 0, 0))
                state = "variable-one";
            else if (ranges.length === 2 && ranges.some(s => word(s, 0, 0)) && ranges.some(s => word(s, 1, 6)))
                state = "variable-both";
            snapshot = "summary.py\n" + state;
        }
        if (snapshot === last) return;
        try {
            writeFileSync(staging, snapshot);
            renameSync(staging, status);
            last = snapshot;
        } catch (error) {
            // A temporary Windows sharing violation must recover even if the user
            // is waiting for the current step and makes no further selection.
            if (!retry && ["EBUSY", "EPERM", "EACCES"].includes(error.code))
                retry = setTimeout(() => { retry = undefined; report(); }, 350);
        }
    };
    context.subscriptions.push(vscode.window.onDidChangeActiveTextEditor(report),
        vscode.window.onDidChangeTextEditorSelection(report), vscode.workspace.onDidChangeTextDocument(report),
        { dispose() { clearTimeout(retry); } });
    report();
}

async function openWorkdayFiles(context) {
    const root = process.env.TOUCH_TYPING_WORKDAY_ROOT;
    const folder = vscode.workspace.workspaceFolders?.[0]?.uri;
    if (!root || !folder || folder.scheme !== "file" || path.relative(root, path.dirname(folder.fsPath)) !== "") return;
    const project = path.basename(folder.fsPath);
    if (!/^files-[a-z]+-\d{3,}-[0-9a-f]{8}$/.test(project)) return;
    const source = await fs.readFile(path.join(folder.fsPath, ".workday-source"), "utf8");
    if (!/^[ac]-source\.txt$/.test(source)) return;
    const document = await vscode.workspace.openTextDocument(path.join(folder.fsPath, source));
    await vscode.window.showTextDocument(document);
    const output = await fs.readFile(path.join(root, ".terminal-" + project), "utf8").catch(error => {
        if (error.code !== "ENOENT") throw error;
    });
    if (!output) return;
    const write = new vscode.EventEmitter();
    const terminal = vscode.window.createTerminal({
        name: "Учебный журнал",
        pty: {
            onDidWrite: write.event,
            open() { write.fire(output.replace(/\n/g, "\r\n")); },
            close() { write.dispose(); }
        }
    });
    context.subscriptions.push(terminal, write);
}

function activate(context) {
    watchWorkdayEditor(context);
    openWorkdayFiles(context).catch(error => console.error("Workday files:", error.message));
    const output = vscode.window.createOutputChannel("Python Lesson");
    context.subscriptions.push(output);
    context.subscriptions.push(vscode.commands.registerCommand("touchTypingStudio.runLesson", async () => {
        try {
            const doc = vscode.window.activeTextEditor?.document;
            const workspace = doc && vscode.workspace.getWorkspaceFolder(doc.uri);
            const python = process.env.TOUCH_TYPING_PYTHON;
            const root = process.env.TOUCH_TYPING_PRACTICE_ROOT;
            const notify = message => vscode.window.showInformationMessage(message);
            if (!doc || !workspace || !python || !root || doc.uri.scheme !== "file") {
                await notify("Откройте файл задания в учебном окне VS Code.");
                return;
            }
            const directory = await fs.realpath(workspace.uri.fsPath);
            const practiceRoot = await fs.realpath(root);
            const source = await fs.realpath(doc.uri.fsPath);
            const model = exercises.get(path.basename(source));
            if (path.dirname(directory) !== practiceRoot || path.dirname(source) !== directory ||
                !/^lesson-[0-9a-f]{7}$/.test(path.basename(directory)) || !model) {
                await notify("Эта команда запускает только учебный файл, открытый из тренажёра.");
                return;
            }
            if (doc.isDirty) {
                await notify("Сначала сохраните файл через Ctrl+S.");
                return;
            }
            const token = (await fs.readFile(path.join(directory, ".lesson-token"), "utf8")).trim();
            if (!/^[0-9a-f]{32}$/.test(token) || !path.basename(directory).endsWith(token.slice(0, 7))) {
                await notify("Задание устарело. Откройте его из тренажёра.");
                return;
            }
            const code = (await fs.readFile(source, "utf8")).replace(/^\uFEFF/, "").replace(/\r\n/g, "\n");
            if (code !== model) {
                await notify("Сначала внесите изменение из карточки и сохраните файл.");
                return;
            }
            // Restricted Mode stays useful here: only these exact lesson programs can run.
            const result = await execute(python, ["-I", "-S", "-c", model], {
                cwd: practiceRoot, windowsHide: true, timeout: 10000, maxBuffer: 16384
            });
            const resultText = result.stdout.replace(/\r\n/g, "\n");
            await fs.writeFile(path.join(directory, ".checked-run.txt"), token + "\n" + resultText, "utf8");
            output.clear();
            output.appendLine("Python · " + path.basename(source));
            output.append(resultText);
            output.show(true);
        } catch (error) {
            await vscode.window.showErrorMessage("Учебный запуск не удался. " + error.message);
        }
    }));
}

module.exports = { activate };
