import { expect, test } from '@playwright/test';
import { readFt4Events, type Ft4AutoEvent } from '../src/Ft4AutoEvents';

function stream(...chunks: string[]) {
  return new ReadableStream<Uint8Array>({
    start(controller) {
      for (const chunk of chunks) controller.enqueue(new TextEncoder().encode(chunk));
      controller.close();
    }
  });
}

test('FT4 progress stream preserves split NDJSON records and terminal receipt', async () => {
  const results: Ft4AutoEvent[] = [];
  await readFt4Events(stream('{"state":"started"}\n{"sta', 'te":"sent","message":"CQ K1ABC FN42"}\n',
    '{"state":"completed","stage":"Complete"}\n'), e => results.push(e));
  expect(results.map(e => e.state)).toEqual(['started', 'sent', 'completed']);
  expect(results[1].message).toBe('CQ K1ABC FN42');
});

test('FT4 unexpected stream termination does not report completed QSO', async () => {
  await expect(readFt4Events(stream('{"state":"sent"}\n'), () => {}))
    .rejects.toThrow('without a terminal receipt');
});
