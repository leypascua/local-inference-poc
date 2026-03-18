import { randomUUID } from 'node:crypto';

function pad(value: number): string {
  return String(value).padStart(2, '0');
}

export function createRequestId(date = new Date()): string {
  const stamp = [
    date.getFullYear(),
    pad(date.getMonth() + 1),
    pad(date.getDate()),
  ].join('') + `T${pad(date.getHours())}${pad(date.getMinutes())}${pad(date.getSeconds())}`;

  return `${stamp}_${randomUUID()}`;
}
