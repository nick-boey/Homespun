import health from '#src/routes/health.js';

describe('GET /health', () => {
  it('returns 200 with status ok', async () => {
    const res = await health.request('/');

    expect(res.status).toBe(200);
    expect(await res.json()).toEqual({ status: 'ok' });
  });
});
