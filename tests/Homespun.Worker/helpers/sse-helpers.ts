export interface SSEEvent {
  event: string;
  data: unknown;
}

export function parseSSEEvents(text: string): SSEEvent[] {
  const events: SSEEvent[] = [];
  const blocks = text.split('\n\n').filter((b) => b.trim().length > 0);

  for (const block of blocks) {
    const lines = block.split('\n');
    let event = '';
    let data = '';

    for (const line of lines) {
      if (line.startsWith('event: ')) {
        event = line.slice('event: '.length);
      } else if (line.startsWith('data: ')) {
        data = line.slice('data: '.length);
      }
    }

    if (event && data) {
      events.push({ event, data: JSON.parse(data) });
    }
  }

  return events;
}
