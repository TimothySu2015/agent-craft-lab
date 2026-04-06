/** 將毫秒數格式化為人類可讀的時間字串（如 "3.2s" 或 "450ms"） */
export function formatDuration(ms: number): string {
  return ms >= 1000 ? `${(ms / 1000).toFixed(1)}s` : `${ms}ms`
}
