const { execFile } = require("node:child_process");
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

function activate(context) {
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
