import { useEffect, useState } from 'react'

/**
 * Returns a value that only updates after the input has been stable for
 * `delayMs`. Used to avoid firing a preview request on every keystroke.
 */
export function useDebouncedValue<T>(value: T, delayMs = 350): T {
  const [debounced, setDebounced] = useState(value)

  useEffect(() => {
    const id = window.setTimeout(() => setDebounced(value), delayMs)
    return () => window.clearTimeout(id)
  }, [value, delayMs])

  return debounced
}
