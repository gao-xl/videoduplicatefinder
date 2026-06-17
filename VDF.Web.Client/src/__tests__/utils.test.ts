import { describe, it, expect } from 'vitest'
import { formatDuration, formatSize, formatBitrate } from '../utils/format'

describe('formatDuration', () => {
  it('formats seconds correctly', () => {
    expect(formatDuration(30)).toBe('30s')
    expect(formatDuration(59)).toBe('59s')
  })

  it('formats minutes correctly', () => {
    expect(formatDuration(60)).toBe('1m 0s')
    expect(formatDuration(90)).toBe('1m 30s')
    expect(formatDuration(3599)).toBe('59m 59s')
  })

  it('formats hours correctly', () => {
    expect(formatDuration(3600)).toBe('1h 0m')
    expect(formatDuration(3661)).toBe('1h 1m')
    expect(formatDuration(7200)).toBe('2h 0m')
  })
})

describe('formatSize', () => {
  it('formats bytes correctly', () => {
    expect(formatSize(0)).toBe('0 B')
    expect(formatSize(512)).toBe('512 B')
    expect(formatSize(1023)).toBe('1023 B')
  })

  it('formats kilobytes correctly', () => {
    expect(formatSize(1024)).toBe('1.0 KB')
    expect(formatSize(1536)).toBe('1.5 KB')
    expect(formatSize(1048575)).toBe('1024.0 KB')
  })

  it('formats megabytes correctly', () => {
    expect(formatSize(1048576)).toBe('1.0 MB')
    expect(formatSize(1572864)).toBe('1.5 MB')
    expect(formatSize(1073741823)).toBe('1024.0 MB')
  })

  it('formats gigabytes correctly', () => {
    expect(formatSize(1073741824)).toBe('1.00 GB')
    expect(formatSize(2147483648)).toBe('2.00 GB')
  })
})

describe('formatBitrate', () => {
  it('formats kbps correctly', () => {
    expect(formatBitrate(500)).toBe('500 Kbps')
    expect(formatBitrate(999)).toBe('999 Kbps')
  })

  it('formats Mbps correctly', () => {
    expect(formatBitrate(1000)).toBe('1.0 Mbps')
    expect(formatBitrate(5000)).toBe('5.0 Mbps')
    expect(formatBitrate(25000)).toBe('25.0 Mbps')
  })
})
