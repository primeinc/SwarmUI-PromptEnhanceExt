/**
 * Minimal OpenAI-compatible backends for the browser gates, started by playwright.config.ts.
 * GET /v1/models lists one model; POST /v1/chat/completions answers `ENHANCED: <last user text>`.
 * The open backend needs no key. The keyed backend answers only requests carrying
 * `Authorization: Bearer <PE_FAKE_BACKEND_KEY>` and returns an OpenAI-style 401 otherwise.
 */
import * as http from 'node:http';

const openPort = Number(process.env.PE_FAKE_BACKEND_PORT ?? 7897);
const keyedPort = Number(process.env.PE_FAKE_KEYED_BACKEND_PORT ?? 7896);
const requiredKey = process.env.PE_FAKE_BACKEND_KEY ?? 'pe-test-key';

/** The model id the fake backends advertise. */
const modelId = 'fake-enhancer';

/** Text of the last user message in an OpenAI chat request: a string, or the text parts of a content array. */
function lastUserText(body: unknown): string {
    const messages = (body as { messages?: { role: string; content: unknown }[] }).messages ?? [];
    const user = [...messages].reverse().find((message) => message.role === 'user');
    if (!user) {
        return '';
    }
    if (typeof user.content === 'string') {
        return user.content;
    }
    if (Array.isArray(user.content)) {
        return user.content
            .filter((part: { type?: string }) => part.type === 'text')
            .map((part: { text?: string }) => part.text ?? '')
            .join(' ');
    }
    return '';
}

function sendJson(res: http.ServerResponse, status: number, payload: unknown): void {
    res.writeHead(status, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify(payload));
}

/** Request handler for one backend; with `key` set, every route requires that bearer token. */
function backend(key: string | null): http.RequestListener {
    return (req, res) => {
        if (key !== null && req.headers.authorization !== `Bearer ${key}`) {
            sendJson(res, 401, { error: { message: 'Incorrect API key provided.', type: 'invalid_request_error', code: 'invalid_api_key' } });
            return;
        }
        if (req.method === 'GET' && req.url === '/v1/models') {
            sendJson(res, 200, { object: 'list', data: [{ id: modelId, object: 'model', created: 0, owned_by: 'tests' }] });
            return;
        }
        if (req.method === 'POST' && req.url === '/v1/chat/completions') {
            const chunks: Buffer[] = [];
            req.on('data', (chunk: Buffer) => chunks.push(chunk));
            req.on('end', () => {
                const body = JSON.parse(Buffer.concat(chunks).toString('utf8'));
                sendJson(res, 200, {
                    id: 'chatcmpl-fake',
                    object: 'chat.completion',
                    model: modelId,
                    choices: [{ index: 0, finish_reason: 'stop', message: { role: 'assistant', content: `ENHANCED: ${lastUserText(body)}` } }],
                });
            });
            return;
        }
        sendJson(res, 404, { error: { message: `no route ${req.method} ${req.url}`, type: 'not_found' } });
    };
}

http.createServer(backend(null)).listen(openPort, '127.0.0.1', () => {
    console.log(`fake backend listening on http://127.0.0.1:${openPort}`);
});
http.createServer(backend(requiredKey)).listen(keyedPort, '127.0.0.1', () => {
    console.log(`fake keyed backend listening on http://127.0.0.1:${keyedPort}`);
});
