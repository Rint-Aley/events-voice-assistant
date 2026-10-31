import '@testing-library/jest-dom/vitest';

Element.prototype.scrollIntoView = () => {};

const memory = new Map<string, string>();
const storage: Storage = {
  get length() {
    return memory.size;
  },
  clear: () => memory.clear(),
  getItem: (key) => memory.get(key) ?? null,
  key: (index) => [...memory.keys()][index] ?? null,
  removeItem: (key) => memory.delete(key),
  setItem: (key, value) => memory.set(key, String(value)),
};

Object.defineProperty(globalThis, 'localStorage', {
  configurable: true,
  value: storage,
});
