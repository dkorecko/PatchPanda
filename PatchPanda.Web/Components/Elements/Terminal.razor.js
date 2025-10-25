export function scrollTerminalToBottom() {
    const terminal = document.getElementById('terminal-output');
    if (terminal) {
        terminal.scrollTop = terminal.scrollHeight;
    }
}
