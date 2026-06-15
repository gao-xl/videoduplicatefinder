import { useState, useCallback, useEffect } from 'react'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import {
  getResults, deleteItems, moveItems, createLinks, removeItems,
  exportCsv, autoSelect, keepBest, thumbnailUrl,
  type DuplicateGroupDto, type DuplicateItemDto, type AutoSelectRequest,
} from '../api/results'
import { Badge } from '../components/shared/Badge'
import { ConfirmDialog } from '../components/shared/ConfirmDialog'
import { Spinner } from '../components/shared/Spinner'
import { CompareModal } from '../components/CompareModal'

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`
}

function formatDuration(seconds: number): string {
  if (!seconds || seconds <= 0) return '—'
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  const s = Math.floor(seconds % 60)
  if (h > 0) return `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
  return `${m}:${String(s).padStart(2, '0')}`
}

function formatBitrate(kbps: number): string {
  if (kbps >= 1000) return `${(kbps / 1000).toFixed(1)} Mbps`
  return `${Math.round(kbps)} Kbps`
}

export function ResultsPage() {
  const qc = useQueryClient()
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [searchInput, setSearchInput] = useState('')
  const [selectedPaths, setSelectedPaths] = useState<Set<string>>(new Set())
  const [collapsedGroups, setCollapsedGroups] = useState<Set<string>>(new Set())
  const [deleteConfirm, setDeleteConfirm] = useState<{ permanent: boolean; paths: string[] } | null>(null)
  const [movePanel, setMovePanel] = useState(false)
  const [moveDestination, setMoveDestination] = useState('')
  const [linkPanel, setLinkPanel] = useState(false)
  const [compareItems, setCompareItems] = useState<DuplicateItemDto[] | null>(null)
  const [contextMenu, setContextMenu] = useState<{ x: number; y: number; item: DuplicateItemDto; group: DuplicateGroupDto } | null>(null)
  const pageSize = 50

  const { data, isLoading } = useQuery({
    queryKey: ['results', page, pageSize, search],
    queryFn: () => getResults(page, pageSize, search),
  })

  const autoSelectMutation = useMutation({
    mutationFn: (mode: AutoSelectRequest) => autoSelect(mode),
    onSuccess: (res) => {
      setSelectedPaths(new Set(res.selectedPaths))
    },
  })

  const keepBestMutation = useMutation({
    mutationFn: (groupId: string) => keepBest({ groupId }),
    onSuccess: (res) => {
      setSelectedPaths(new Set(res.selectedPaths))
    },
  })

  const deleteMutation = useMutation({
    mutationFn: (req: { paths: string[]; permanent: boolean }) => deleteItems(req),
    onSuccess: () => {
      setSelectedPaths(new Set())
      qc.invalidateQueries({ queryKey: ['results'] })
    },
  })

  const moveMutation = useMutation({
    mutationFn: (req: { paths: string[]; destination: string }) => moveItems(req),
    onSuccess: () => {
      setSelectedPaths(new Set())
      setMovePanel(false)
      qc.invalidateQueries({ queryKey: ['results'] })
    },
  })

  const linkMutation = useMutation({
    mutationFn: (req: { paths: string[]; hardlink: boolean }) => createLinks(req),
    onSuccess: () => {
      setSelectedPaths(new Set())
      setLinkPanel(false)
      qc.invalidateQueries({ queryKey: ['results'] })
    },
  })

  const removeMutation = useMutation({
    mutationFn: (paths: string[]) => removeItems({ paths }),
    onSuccess: () => {
      setSelectedPaths(new Set())
      qc.invalidateQueries({ queryKey: ['results'] })
    },
  })

  const handleSearch = useCallback(() => {
    setSearch(searchInput)
    setPage(1)
  }, [searchInput])

  const toggleSelect = useCallback((path: string) => {
    setSelectedPaths(prev => {
      const next = new Set(prev)
      if (next.has(path)) next.delete(path)
      else next.add(path)
      return next
    })
  }, [])

  const toggleGroupSelect = useCallback((group: DuplicateGroupDto) => {
    const allSelected = group.items.every(i => selectedPaths.has(i.path))
    setSelectedPaths(prev => {
      const next = new Set(prev)
      group.items.forEach(i => {
        if (allSelected) next.delete(i.path)
        else next.add(i.path)
      })
      return next
    })
  }, [selectedPaths])

  const toggleCollapse = useCallback((groupId: string) => {
    setCollapsedGroups(prev => {
      const next = new Set(prev)
      if (next.has(groupId)) next.delete(groupId)
      else next.add(groupId)
      return next
    })
  }, [])

  const collapseAll = useCallback(() => {
    if (data?.groups) {
      setCollapsedGroups(new Set(data.groups.map(g => g.groupId)))
    }
  }, [data])

  const expandAll = useCallback(() => {
    setCollapsedGroups(new Set())
  }, [])

  const handleExport = useCallback(async () => {
    const blob = await exportCsv()
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = 'vdf-results.csv'
    a.click()
    URL.revokeObjectURL(url)
  }, [])

  // Close context menu on click outside
  useEffect(() => {
    if (!contextMenu) return
    const handler = () => setContextMenu(null)
    document.addEventListener('click', handler)
    return () => document.removeEventListener('click', handler)
  }, [contextMenu])

  const [focusedGroupIdx, setFocusedGroupIdx] = useState(-1)

  // Keyboard navigation
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement || e.target instanceof HTMLSelectElement) return
      const groups = data?.groups ?? []
      if (e.key === 'Escape') {
        setSelectedPaths(new Set())
        setContextMenu(null)
        return
      }
      // j/k: navigate between groups
      if (e.key === 'j' || e.key === 'J') {
        e.preventDefault()
        const next = Math.min(focusedGroupIdx + 1, groups.length - 1)
        setFocusedGroupIdx(next)
        document.getElementById(`group-${next}`)?.scrollIntoView({ behavior: 'smooth', block: 'nearest' })
        return
      }
      if (e.key === 'k' || e.key === 'K') {
        e.preventDefault()
        const prev = Math.max(focusedGroupIdx - 1, 0)
        setFocusedGroupIdx(prev)
        document.getElementById(`group-${prev}`)?.scrollIntoView({ behavior: 'smooth', block: 'nearest' })
        return
      }
      // x: keep best in focused group (select all except highest bitrate)
      if ((e.key === 'x' || e.key === 'X') && focusedGroupIdx >= 0 && focusedGroupIdx < groups.length) {
        e.preventDefault()
        const group = groups[focusedGroupIdx]
        if (!group || group.items.length < 2) return
        // Pick the item with the highest bitrate as the keeper
        const best = group.items.reduce((a, b) => b.bitRateKbs > a.bitRateKbs ? b : a, group.items[0]!)
        if (!best) return
        const newSel = new Set(selectedPaths)
        group.items.forEach(item => {
          if (item.path !== best.path) newSel.add(item.path)
        })
        setSelectedPaths(newSel)
        return
      }
      // 1-9: toggle selection of item in focused group
      if (/^[1-9]$/.test(e.key) && focusedGroupIdx >= 0 && focusedGroupIdx < groups.length) {
        const idx = parseInt(e.key) - 1
        const group = groups[focusedGroupIdx]
        if (group && idx < group.items.length) {
          const item = group.items[idx]
          if (item) {
            const newSel = new Set(selectedPaths)
            if (newSel.has(item.path)) newSel.delete(item.path)
            else newSel.add(item.path)
            setSelectedPaths(newSel)
          }
        }
        return
      }
    }
    document.addEventListener('keydown', handler)
    return () => document.removeEventListener('keydown', handler)
  }, [focusedGroupIdx, data, selectedPaths])

  const groups = data?.groups ?? []
  const selectedCount = selectedPaths.size

  return (
    <div style={{ animation: 'fadeIn 0.4s ease' }}>
      {/* Search & Stats Bar */}
      <div style={{
        background: 'var(--bg-surface)',
        border: '1px solid var(--border-default)',
        borderRadius: 'var(--radius-xl)',
        padding: '0.85rem 1.25rem',
        marginBottom: '1rem',
        display: 'flex',
        alignItems: 'center',
        gap: '1.25rem',
        flexWrap: 'wrap',
        boxShadow: '0 0 0 1px var(--accent-primary-glow), var(--shadow-sm)',
        position: 'relative',
        overflow: 'hidden',
      }}>
        {/* Subtle top glow line */}
        <div style={{
          position: 'absolute',
          top: 0,
          left: '10%',
          right: '10%',
          height: '1px',
          background: 'linear-gradient(90deg, transparent, var(--accent-primary), transparent)',
          opacity: 0.4,
        }} />

        <div style={{ display: 'flex', gap: '0.4rem', flex: 1, minWidth: 220 }}>
          <div style={{
            flex: 1,
            position: 'relative',
            display: 'flex',
            alignItems: 'center',
          }}>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="var(--text-dim)" strokeWidth="2" style={{
              position: 'absolute',
              left: '0.65rem',
              pointerEvents: 'none',
            }}>
              <circle cx="11" cy="11" r="8" />
              <line x1="21" y1="21" x2="16.65" y2="16.65" />
            </svg>
            <input
              value={searchInput}
              onChange={e => setSearchInput(e.target.value)}
              onKeyDown={e => { if (e.key === 'Enter') handleSearch() }}
              placeholder="Search files..."
              style={{
                flex: 1,
                padding: '0.5rem 0.65rem 0.5rem 2rem',
                border: '1px solid var(--border-input)',
                borderRadius: 'var(--radius-md)',
                background: 'var(--bg-input)',
                color: 'var(--text-primary)',
                fontSize: '0.85rem',
                fontFamily: 'var(--font-sans)',
                outline: 'none',
                transition: 'border-color var(--transition-fast), box-shadow var(--transition-fast)',
              }}
              onFocus={e => {
                e.currentTarget.style.borderColor = 'var(--accent-primary)'
                e.currentTarget.style.boxShadow = '0 0 0 3px var(--accent-primary-glow)'
              }}
              onBlur={e => {
                e.currentTarget.style.borderColor = 'var(--border-input)'
                e.currentTarget.style.boxShadow = 'none'
              }}
            />
          </div>
          <button
            onClick={handleSearch}
            style={{
              padding: '0.5rem 1rem',
              borderRadius: 'var(--radius-md)',
              border: '1px solid var(--accent-primary)',
              background: 'rgba(14, 165, 233, 0.08)',
              color: 'var(--accent-primary)',
              cursor: 'pointer',
              fontSize: '0.8rem',
              fontFamily: 'var(--font-sans)',
              fontWeight: 500,
              transition: 'background var(--transition-fast), box-shadow var(--transition-fast)',
              whiteSpace: 'nowrap',
            }}
            onMouseEnter={e => {
              e.currentTarget.style.background = 'rgba(14, 165, 233, 0.18)'
              e.currentTarget.style.boxShadow = '0 0 12px var(--accent-primary-glow)'
            }}
            onMouseLeave={e => {
              e.currentTarget.style.background = 'rgba(14, 165, 233, 0.08)'
              e.currentTarget.style.boxShadow = 'none'
            }}
          >
            Search
          </button>
        </div>

        {data && (
          <div style={{
            display: 'flex',
            gap: '1.25rem',
            fontSize: '0.75rem',
            color: 'var(--text-muted)',
            fontFamily: 'var(--font-sans)',
          }}>
            <span style={{ display: 'flex', alignItems: 'center', gap: '0.35rem' }}>
              <span style={{ fontFamily: 'var(--font-display)', fontWeight: 600, fontSize: '1rem', color: 'var(--accent-primary)' }}>{data.totalGroups}</span>
              groups
            </span>
            <span style={{ display: 'flex', alignItems: 'center', gap: '0.35rem' }}>
              <span style={{ fontFamily: 'var(--font-display)', fontWeight: 600, fontSize: '1rem', color: 'var(--accent-primary)' }}>{data.totalFiles}</span>
              files
            </span>
            <span style={{ display: 'flex', alignItems: 'center', gap: '0.35rem' }}>
              <span style={{ fontFamily: 'var(--font-display)', fontWeight: 600, fontSize: '1rem', color: 'var(--text-primary)' }}>{formatSize(data.totalSizeBytes)}</span>
              total
            </span>
            <span style={{
              display: 'flex',
              alignItems: 'center',
              gap: '0.35rem',
              color: 'var(--accent-success-text)',
              background: 'var(--accent-success-bg)',
              border: '1px solid var(--accent-success-border)',
              padding: '0.15rem 0.6rem',
              borderRadius: 'var(--radius-md)',
              fontFamily: 'var(--font-sans)',
            }}>
              <span style={{ fontFamily: 'var(--font-display)', fontWeight: 600, fontSize: '0.85rem' }}>{formatSize(data.potentialSavingsBytes)}</span>
              savings
            </span>
          </div>
        )}
      </div>

      {/* Toolbar */}
      <div style={{
        display: 'flex',
        alignItems: 'center',
        gap: '0.5rem',
        marginBottom: '1rem',
        flexWrap: 'wrap',
      }}>
        {/* Auto-select */}
        <select
          onChange={e => {
            if (e.target.value) autoSelectMutation.mutate({ mode: e.target.value as AutoSelectRequest['mode'] })
          }}
          defaultValue=""
          style={{
            padding: '0.4rem 0.7rem',
            border: '1px solid var(--border-default)',
            borderRadius: 'var(--radius-md)',
            background: 'var(--bg-button)',
            color: 'var(--text-secondary)',
            fontSize: '0.8rem',
            fontFamily: 'var(--font-sans)',
            cursor: 'pointer',
            outline: 'none',
            transition: 'border-color var(--transition-fast)',
          }}
          onFocus={e => { e.currentTarget.style.borderColor = 'var(--accent-primary)' }}
          onBlur={e => { e.currentTarget.style.borderColor = 'var(--border-default)' }}
        >
          <option value="" disabled>Auto-select...</option>
          <option value="lowestQuality">Lowest quality</option>
          <option value="smallestFile">Smallest file</option>
          <option value="oldest">Oldest</option>
          <option value="newest">Newest</option>
          <option value="hundredPercentEqual">100% equal</option>
        </select>

        <button onClick={collapseAll} style={toolbarBtnStyle}>Collapse all</button>
        <button onClick={expandAll} style={toolbarBtnStyle}>Expand all</button>
        <button onClick={handleExport} style={toolbarBtnStyle}>Export CSV</button>

        {selectedCount > 0 && (
          <span style={{
            fontSize: '0.75rem',
            color: 'var(--accent-primary)',
            fontWeight: 600,
            fontFamily: 'var(--font-display)',
            marginLeft: '0.25rem',
            background: 'var(--accent-primary-glow)',
            padding: '0.2rem 0.6rem',
            borderRadius: 'var(--radius-md)',
            border: '1px solid rgba(14, 165, 233, 0.25)',
          }}>
            {selectedCount} selected
          </span>
        )}

        <div style={{ flex: 1 }} />

        {selectedCount > 0 && (
          <div style={{ display: 'flex', gap: '0.4rem' }}>
            <button
              onClick={() => setDeleteConfirm({ permanent: false, paths: [...selectedPaths] })}
              style={{
                ...toolbarBtnStyle,
                color: 'var(--accent-danger-text)',
                borderColor: 'var(--accent-error-border)',
                background: 'var(--accent-error-bg)',
              }}
            >
              Trash
            </button>
            <button
              onClick={() => setDeleteConfirm({ permanent: true, paths: [...selectedPaths] })}
              style={{
                ...toolbarBtnStyle,
                color: 'var(--accent-danger-text)',
                borderColor: 'var(--accent-error-border)',
                background: 'var(--accent-error-bg)',
              }}
            >
              Delete
            </button>
            <button onClick={() => setMovePanel(true)} style={toolbarBtnStyle}>Move</button>
            <button onClick={() => setLinkPanel(true)} style={toolbarBtnStyle}>Link</button>
            <button
              onClick={() => removeMutation.mutate([...selectedPaths])}
              style={toolbarBtnStyle}
            >
              Remove
            </button>
          </div>
        )}
      </div>

      {/* Move Panel */}
      {movePanel && (
        <div style={{
          background: 'var(--bg-surface)',
          border: '1px solid var(--accent-primary)',
          borderRadius: 'var(--radius-lg)',
          padding: '1rem 1.25rem',
          marginBottom: '1rem',
          display: 'flex',
          gap: '0.65rem',
          alignItems: 'center',
          boxShadow: '0 0 20px var(--accent-primary-glow), var(--shadow-md)',
          animation: 'cardIn 0.25s ease',
        }}>
          <span style={{
            fontSize: '0.85rem',
            color: 'var(--text-secondary)',
            whiteSpace: 'nowrap',
            fontFamily: 'var(--font-sans)',
            fontWeight: 500,
          }}>
            Move to:
          </span>
          <input
            value={moveDestination}
            onChange={e => setMoveDestination(e.target.value)}
            placeholder="Destination folder..."
            style={{
              flex: 1,
              padding: '0.5rem 0.75rem',
              border: '1px solid var(--border-input)',
              borderRadius: 'var(--radius-md)',
              background: 'var(--bg-input)',
              color: 'var(--text-primary)',
              fontSize: '0.85rem',
              fontFamily: 'var(--font-mono)',
              outline: 'none',
              transition: 'border-color var(--transition-fast), box-shadow var(--transition-fast)',
            }}
            onFocus={e => {
              e.currentTarget.style.borderColor = 'var(--accent-primary)'
              e.currentTarget.style.boxShadow = '0 0 0 3px var(--accent-primary-glow)'
            }}
            onBlur={e => {
              e.currentTarget.style.borderColor = 'var(--border-input)'
              e.currentTarget.style.boxShadow = 'none'
            }}
          />
          <button
            onClick={() => moveMutation.mutate({ paths: [...selectedPaths], destination: moveDestination })}
            disabled={!moveDestination.trim() || moveMutation.isPending}
            style={{
              padding: '0.5rem 1rem',
              borderRadius: 'var(--radius-md)',
              border: 'none',
              background: 'var(--accent-primary)',
              color: '#fff',
              cursor: moveMutation.isPending ? 'wait' : 'pointer',
              fontSize: '0.8rem',
              fontFamily: 'var(--font-sans)',
              fontWeight: 500,
              opacity: (!moveDestination.trim() || moveMutation.isPending) ? 0.5 : 1,
              transition: 'box-shadow var(--transition-fast)',
            }}
            onMouseEnter={e => { if (moveDestination.trim() && !moveMutation.isPending) e.currentTarget.style.boxShadow = '0 0 16px var(--accent-primary-glow)' }}
            onMouseLeave={e => { e.currentTarget.style.boxShadow = 'none' }}
          >
            {moveMutation.isPending ? 'Moving...' : 'Move'}
          </button>
          <button onClick={() => setMovePanel(false)} style={toolbarBtnStyle}>Cancel</button>
        </div>
      )}

      {/* Link Panel */}
      {linkPanel && (
        <div style={{
          background: 'var(--bg-surface)',
          border: '1px solid var(--accent-primary)',
          borderRadius: 'var(--radius-lg)',
          padding: '1rem 1.25rem',
          marginBottom: '1rem',
          display: 'flex',
          gap: '0.65rem',
          alignItems: 'center',
          boxShadow: '0 0 20px var(--accent-primary-glow), var(--shadow-md)',
          animation: 'cardIn 0.25s ease',
        }}>
          <span style={{
            fontSize: '0.85rem',
            color: 'var(--text-secondary)',
            fontFamily: 'var(--font-sans)',
            fontWeight: 500,
          }}>
            Replace with:
          </span>
          <button
            onClick={() => linkMutation.mutate({ paths: [...selectedPaths], hardlink: true })}
            disabled={linkMutation.isPending}
            style={{
              padding: '0.5rem 1rem',
              borderRadius: 'var(--radius-md)',
              border: 'none',
              background: 'var(--accent-primary)',
              color: '#fff',
              cursor: linkMutation.isPending ? 'wait' : 'pointer',
              fontSize: '0.8rem',
              fontFamily: 'var(--font-sans)',
              fontWeight: 500,
              opacity: linkMutation.isPending ? 0.5 : 1,
              transition: 'box-shadow var(--transition-fast)',
            }}
            onMouseEnter={e => { if (!linkMutation.isPending) e.currentTarget.style.boxShadow = '0 0 16px var(--accent-primary-glow)' }}
            onMouseLeave={e => { e.currentTarget.style.boxShadow = 'none' }}
          >
            Hardlinks
          </button>
          <button
            onClick={() => linkMutation.mutate({ paths: [...selectedPaths], hardlink: false })}
            disabled={linkMutation.isPending}
            style={{
              padding: '0.5rem 1rem',
              borderRadius: 'var(--radius-md)',
              border: '1px solid var(--accent-primary)',
              background: 'rgba(14, 165, 233, 0.08)',
              color: 'var(--accent-primary)',
              cursor: linkMutation.isPending ? 'wait' : 'pointer',
              fontSize: '0.8rem',
              fontFamily: 'var(--font-sans)',
              fontWeight: 500,
              opacity: linkMutation.isPending ? 0.5 : 1,
              transition: 'background var(--transition-fast), box-shadow var(--transition-fast)',
            }}
            onMouseEnter={e => {
              if (!linkMutation.isPending) {
                e.currentTarget.style.background = 'rgba(14, 165, 233, 0.18)'
                e.currentTarget.style.boxShadow = '0 0 12px var(--accent-primary-glow)'
              }
            }}
            onMouseLeave={e => {
              e.currentTarget.style.background = 'rgba(14, 165, 233, 0.08)'
              e.currentTarget.style.boxShadow = 'none'
            }}
          >
            Symlinks
          </button>
          <button onClick={() => setLinkPanel(false)} style={toolbarBtnStyle}>Cancel</button>
        </div>
      )}

      {/* Loading */}
      {isLoading && (
        <div style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          padding: '4rem',
          color: 'var(--text-muted)',
          gap: '0.75rem',
          fontFamily: 'var(--font-sans)',
          fontSize: '0.9rem',
        }}>
          <Spinner size={20} />
          Loading results...
        </div>
      )}

      {/* Empty State */}
      {!isLoading && groups.length === 0 && (
        <div style={{
          textAlign: 'center',
          padding: '5rem 2rem',
          color: 'var(--text-dim)',
        }}>
          <div style={{
            width: 80,
            height: 80,
            margin: '0 auto 1.5rem',
            borderRadius: 'var(--radius-xl)',
            background: 'var(--bg-surface)',
            border: '1px solid var(--border-subtle)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            boxShadow: 'var(--shadow-md)',
          }}>
            <svg width="36" height="36" viewBox="0 0 24 24" fill="none" stroke="var(--text-dim)" strokeWidth="1.5" style={{ opacity: 0.5 }}>
              <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
              <polyline points="14 2 14 8 20 8" />
            </svg>
          </div>
          <div style={{
            fontSize: '1.1rem',
            color: 'var(--text-muted)',
            marginBottom: '0.5rem',
            fontFamily: 'var(--font-display)',
            fontWeight: 600,
          }}>
            No duplicate groups found
          </div>
          <div style={{
            fontSize: '0.85rem',
            fontFamily: 'var(--font-sans)',
            color: 'var(--text-dim)',
          }}>
            Run a scan first to detect duplicate videos
          </div>
        </div>
      )}

      {/* Duplicate Groups */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
        {groups.map((group, gi) => (
          <div
            key={group.groupId}
            id={`group-${gi}`}
            style={{
              outline: gi === focusedGroupIdx ? '2px solid var(--accent-primary, #6C5CE7)' : 'none',
              outlineOffset: '2px',
              borderRadius: '12px',
            }}
          >
            <DuplicateGroupCard
              group={group}
              index={gi}
              collapsed={collapsedGroups.has(group.groupId)}
              selectedPaths={selectedPaths}
              onToggleCollapse={() => toggleCollapse(group.groupId)}
              onToggleGroupSelect={() => toggleGroupSelect(group)}
              onToggleSelect={toggleSelect}
              onKeepBest={() => keepBestMutation.mutate(group.groupId)}
              onCompare={() => setCompareItems(group.items)}
              onContextMenu={(e, item) => {
                e.preventDefault()
                setContextMenu({ x: e.clientX, y: e.clientY, item, group })
              }}
            />
          </div>
        ))}
      </div>

      {/* Pagination */}
      {data && data.totalGroups > pageSize && (
        <div style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          gap: '0.75rem',
          marginTop: '2rem',
          padding: '1rem 0',
        }}>
          <button
            onClick={() => setPage(p => Math.max(1, p - 1))}
            disabled={page === 1}
            style={{
              ...toolbarBtnStyle,
              opacity: page === 1 ? 0.35 : 1,
              cursor: page === 1 ? 'not-allowed' : 'pointer',
              padding: '0.45rem 1rem',
              borderRadius: 'var(--radius-md)',
            }}
          >
            ← Previous
          </button>
          <div style={{
            display: 'flex',
            alignItems: 'center',
            gap: '0.5rem',
            fontFamily: 'var(--font-display)',
            fontSize: '0.85rem',
            color: 'var(--text-secondary)',
            background: 'var(--bg-surface)',
            border: '1px solid var(--border-subtle)',
            borderRadius: 'var(--radius-md)',
            padding: '0.4rem 1rem',
          }}>
            <span style={{ color: 'var(--accent-primary)', fontWeight: 600 }}>{page}</span>
            <span style={{ color: 'var(--text-dim)' }}>of</span>
            <span style={{ fontWeight: 600 }}>{Math.ceil(data.totalGroups / pageSize)}</span>
          </div>
          <button
            onClick={() => setPage(p => p + 1)}
            disabled={page >= Math.ceil(data.totalGroups / pageSize)}
            style={{
              ...toolbarBtnStyle,
              opacity: page >= Math.ceil(data.totalGroups / pageSize) ? 0.35 : 1,
              cursor: page >= Math.ceil(data.totalGroups / pageSize) ? 'not-allowed' : 'pointer',
              padding: '0.45rem 1rem',
              borderRadius: 'var(--radius-md)',
            }}
          >
            Next →
          </button>
        </div>
      )}

      {/* Context Menu */}
      {contextMenu && (
        <div
          style={{
            position: 'fixed',
            left: contextMenu.x,
            top: contextMenu.y,
            zIndex: 1100,
            background: 'var(--bg-surface-raised)',
            border: '1px solid var(--border-default)',
            borderRadius: 'var(--radius-lg)',
            boxShadow: 'var(--shadow-lg), 0 0 24px rgba(0,0,0,0.4)',
            padding: '0.3rem 0',
            minWidth: 200,
            animation: 'fadeIn 0.12s ease',
            backdropFilter: 'blur(12px)',
          }}
        >
          {[
            { label: 'Open file', action: () => { /* no-op in web */ } },
            { label: 'Open folder', action: () => { /* no-op in web */ } },
            { label: 'Copy path', action: () => navigator.clipboard.writeText(contextMenu.item.path) },
            { label: selectedPaths.has(contextMenu.item.path) ? 'Deselect' : 'Select', action: () => toggleSelect(contextMenu.item.path) },
            { label: 'Hide from results', action: () => removeMutation.mutate([contextMenu.item.path]), danger: true },
          ].map((item, i) => (
            <button
              key={i}
              onClick={() => { item.action(); setContextMenu(null) }}
              style={{
                display: 'block',
                width: '100%',
                padding: '0.45rem 0.85rem',
                border: 'none',
                background: 'transparent',
                color: 'danger' in item && item.danger ? 'var(--accent-danger-text)' : 'var(--text-secondary)',
                cursor: 'pointer',
                fontSize: '0.8rem',
                textAlign: 'left',
                fontFamily: 'var(--font-sans)',
                transition: 'background var(--transition-fast), color var(--transition-fast)',
              }}
              onMouseEnter={e => {
                if ('danger' in item && item.danger) {
                  e.currentTarget.style.background = 'var(--accent-error-bg)'
                } else {
                  e.currentTarget.style.background = 'var(--bg-hover)'
                  e.currentTarget.style.color = 'var(--text-primary)'
                }
              }}
              onMouseLeave={e => {
                e.currentTarget.style.background = 'transparent'
                if (!('danger' in item && item.danger)) {
                  e.currentTarget.style.color = 'var(--text-secondary)'
                }
              }}
            >
              {item.label}
            </button>
          ))}
        </div>
      )}

      {/* Delete Confirm */}
      <ConfirmDialog
        open={deleteConfirm !== null}
        title={deleteConfirm?.permanent ? 'Delete Permanently' : 'Move to Trash'}
        message={
          deleteConfirm?.permanent
            ? `Permanently delete ${deleteConfirm.paths.length} file(s)? This cannot be undone.`
            : `Move ${deleteConfirm?.paths.length} file(s) to trash?`
        }
        confirmLabel={deleteConfirm?.permanent ? 'Delete Permanently' : 'Move to Trash'}
        variant="danger"
        onConfirm={() => {
          if (deleteConfirm) {
            deleteMutation.mutate({ paths: deleteConfirm.paths, permanent: deleteConfirm.permanent })
          }
          setDeleteConfirm(null)
        }}
        onCancel={() => setDeleteConfirm(null)}
      />

      {/* Compare Modal */}
      {compareItems && (
        <CompareModal
          items={compareItems}
          onClose={() => setCompareItems(null)}
        />
      )}

      <style>{`
        @keyframes fadeIn {
          from { opacity: 0; }
          to { opacity: 1; }
        }
        @keyframes cardIn {
          from { opacity: 0; transform: translateY(10px); }
          to { opacity: 1; transform: translateY(0); }
        }
      `}</style>
    </div>
  )
}

const toolbarBtnStyle: React.CSSProperties = {
  padding: '0.4rem 0.7rem',
  borderRadius: 'var(--radius-md)',
  border: '1px solid var(--border-default)',
  background: 'var(--bg-button)',
  color: 'var(--text-secondary)',
  cursor: 'pointer',
  fontSize: '0.8rem',
  fontFamily: 'var(--font-sans)',
  whiteSpace: 'nowrap',
  transition: 'background var(--transition-fast), border-color var(--transition-fast), box-shadow var(--transition-fast), color var(--transition-fast)',
}

interface DuplicateGroupCardProps {
  group: DuplicateGroupDto
  index: number
  collapsed: boolean
  selectedPaths: Set<string>
  onToggleCollapse: () => void
  onToggleGroupSelect: () => void
  onToggleSelect: (path: string) => void
  onKeepBest: () => void
  onCompare: () => void
  onContextMenu: (e: React.MouseEvent, item: DuplicateItemDto) => void
}

function DuplicateGroupCard({
  group, index, collapsed, selectedPaths,
  onToggleCollapse, onToggleGroupSelect, onToggleSelect,
  onKeepBest, onCompare, onContextMenu,
}: DuplicateGroupCardProps) {
  const allSelected = group.items.every(i => selectedPaths.has(i.path))
  const someSelected = group.items.some(i => selectedPaths.has(i.path))
  const totalSize = group.items.reduce((sum, i) => sum + i.sizeBytes, 0)
  const maxSimilarity = Math.max(...group.items.map(i => i.similarity))

  // Determine similarity accent color for left border
  const similarityColor = maxSimilarity >= 99.5
    ? 'var(--accent-success-text)'
    : maxSimilarity >= 95
      ? 'var(--accent-warning)'
      : 'var(--accent-primary)'

  return (
    <div style={{
      background: 'var(--bg-surface)',
      border: `1px solid ${someSelected ? 'var(--accent-primary)' : 'var(--card-border)'}`,
      borderLeft: `3px solid ${similarityColor}`,
      borderRadius: 'var(--radius-lg)',
      overflow: 'hidden',
      transition: 'border-color var(--transition-fast), box-shadow var(--transition-fast)',
      animation: `cardIn 0.35s ease ${index * 0.04}s both`,
      boxShadow: someSelected ? '0 0 16px var(--accent-primary-glow)' : 'var(--shadow-sm)',
    }}
      onMouseEnter={e => {
        if (!someSelected) {
          e.currentTarget.style.borderColor = 'var(--card-hover-border)'
          e.currentTarget.style.borderLeftColor = similarityColor
          e.currentTarget.style.boxShadow = 'var(--shadow-md)'
        }
      }}
      onMouseLeave={e => {
        if (!someSelected) {
          e.currentTarget.style.borderColor = 'var(--card-border)'
          e.currentTarget.style.borderLeftColor = similarityColor
          e.currentTarget.style.boxShadow = 'var(--shadow-sm)'
        }
      }}
    >
      {/* Group Header */}
      <div style={{
        display: 'flex',
        alignItems: 'center',
        gap: '0.5rem',
        padding: '0.65rem 1rem',
        borderBottom: collapsed ? 'none' : '1px solid var(--border-subtle)',
        background: 'var(--bg-surface-raised)',
      }}>
        <input
          type="checkbox"
          checked={allSelected}
          ref={el => { if (el) el.indeterminate = someSelected && !allSelected }}
          onChange={onToggleGroupSelect}
          style={{ accentColor: 'var(--accent-primary)', cursor: 'pointer' }}
        />

        <span style={{
          fontFamily: 'var(--font-display)',
          fontWeight: 600,
          fontSize: '0.9rem',
          color: 'var(--text-primary)',
        }}>
          {group.items.length} files
        </span>

        <span style={{
          fontSize: '0.75rem',
          color: 'var(--text-muted)',
          fontFamily: 'var(--font-mono)',
        }}>
          {formatSize(totalSize)}
        </span>

        <Badge variant={maxSimilarity >= 99.5 ? 'success' : maxSimilarity >= 95 ? 'warning' : 'default'}>
          {maxSimilarity.toFixed(1)}%
        </Badge>

        <div style={{ flex: 1 }} />

        <button onClick={onKeepBest} style={miniBtnStyle} title="Keep best quality">
          Keep best
        </button>
        <button onClick={onCompare} style={miniBtnStyle} title="Compare">
          Compare
        </button>
        <button
          onClick={onToggleCollapse}
          style={{
            ...miniBtnStyle,
            fontSize: 10,
            padding: '0.25rem 0.45rem',
            fontFamily: 'var(--font-mono)',
          }}
        >
          {collapsed ? '▸' : '▾'}
        </button>
      </div>

      {/* Items */}
      {!collapsed && (
        <div style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))',
          gap: '0.65rem',
          padding: '0.85rem',
        }}>
          {group.items.map(item => (
            <DuplicateItemCard
              key={item.path}
              item={item}
              selected={selectedPaths.has(item.path)}
              onToggleSelect={() => onToggleSelect(item.path)}
              onContextMenu={e => onContextMenu(e, item)}
              groupItems={group.items}
            />
          ))}
        </div>
      )}
    </div>
  )
}

const miniBtnStyle: React.CSSProperties = {
  padding: '0.25rem 0.55rem',
  borderRadius: 'var(--radius-sm)',
  border: '1px solid var(--border-default)',
  background: 'var(--bg-button)',
  color: 'var(--text-muted)',
  cursor: 'pointer',
  fontSize: '0.7rem',
  fontFamily: 'var(--font-sans)',
  whiteSpace: 'nowrap',
  transition: 'background var(--transition-fast), border-color var(--transition-fast), color var(--transition-fast)',
}

interface DuplicateItemCardProps {
  item: DuplicateItemDto
  selected: boolean
  onToggleSelect: () => void
  onContextMenu: (e: React.MouseEvent) => void
  groupItems: DuplicateItemDto[]
}

function DuplicateItemCard({ item, selected, onToggleSelect, onContextMenu, groupItems }: DuplicateItemCardProps) {
  const [imgLoaded, setImgLoaded] = useState(false)
  const [imgError, setImgError] = useState(false)
  const thumbSrc = thumbnailUrl(item.path)

  // Find best values in group for comparison highlighting
  const bestSize = Math.max(...groupItems.map(i => i.sizeBytes))
  const bestBitrate = Math.max(...groupItems.map(i => i.bitRateKbs))

  const isBestSize = item.sizeBytes === bestSize && groupItems.filter(i => i.sizeBytes === bestSize).length === 1
  const isBestBitrate = item.bitRateKbs === bestBitrate && groupItems.filter(i => i.bitRateKbs === bestBitrate).length === 1

  const fileName = item.path.split(/[/\\]/).pop() || item.path

  return (
    <div
      onClick={e => { if (!e.shiftKey) onToggleSelect() }}
      onContextMenu={onContextMenu}
      style={{
        background: selected ? 'var(--bg-selected)' : 'var(--bg-surface-raised)',
        border: `1px solid ${selected ? 'var(--accent-primary)' : 'var(--border-subtle)'}`,
        borderRadius: 'var(--radius-md)',
        overflow: 'hidden',
        cursor: 'pointer',
        transition: 'background var(--transition-fast), border-color var(--transition-fast), transform var(--transition-fast), box-shadow var(--transition-fast)',
        position: 'relative',
        boxShadow: selected ? '0 0 12px var(--accent-primary-glow)' : 'var(--shadow-sm)',
      }}
      onMouseEnter={e => {
        if (!selected) {
          e.currentTarget.style.borderColor = 'var(--card-hover-border)'
          e.currentTarget.style.transform = 'translateY(-2px)'
          e.currentTarget.style.boxShadow = 'var(--shadow-md)'
        }
      }}
      onMouseLeave={e => {
        if (!selected) {
          e.currentTarget.style.borderColor = 'var(--border-subtle)'
          e.currentTarget.style.transform = 'translateY(0)'
          e.currentTarget.style.boxShadow = 'var(--shadow-sm)'
        }
      }}
    >
      {/* Thumbnail */}
      <div style={{
        position: 'relative',
        width: '100%',
        aspectRatio: '16/9',
        background: 'var(--bg-input)',
        overflow: 'hidden',
        borderRadius: 'var(--radius-sm) var(--radius-sm) 0 0',
        margin: '0.35rem 0.35rem 0',
      }}>
        {!imgError && (
          <img
            src={thumbSrc}
            alt=""
            loading="lazy"
            onLoad={() => setImgLoaded(true)}
            onError={() => setImgError(true)}
            style={{
              width: '100%',
              height: '100%',
              objectFit: 'cover',
              opacity: imgLoaded ? 1 : 0,
              transition: 'opacity 0.3s ease',
              borderRadius: 'var(--radius-sm) var(--radius-sm) 0 0',
            }}
          />
        )}
        {!imgLoaded && !imgError && (
          <div style={{
            position: 'absolute',
            inset: 0,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
          }}>
            <Spinner size={16} />
          </div>
        )}
        {imgError && (
          <div style={{
            position: 'absolute',
            inset: 0,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            color: 'var(--text-dim)',
            fontSize: '0.7rem',
            fontFamily: 'var(--font-sans)',
          }}>
            No thumbnail
          </div>
        )}

        {/* Overlay badges */}
        <div style={{
          position: 'absolute',
          top: 4,
          left: 4,
          display: 'flex',
          gap: '0.25rem',
        }}>
          {isBestBitrate && <Badge variant="best">Best</Badge>}
          {item.flags !== 'None' && <Badge variant="clip">Clip</Badge>}
        </div>

        {/* Duration badge */}
        <div style={{
          position: 'absolute',
          bottom: 4,
          right: 4,
          background: 'rgba(0,0,0,0.75)',
          color: '#fff',
          padding: '0.15rem 0.4rem',
          borderRadius: 'var(--radius-sm)',
          fontSize: 10,
          fontFamily: 'var(--font-mono)',
          backdropFilter: 'blur(4px)',
          letterSpacing: '0.02em',
        }}>
          {formatDuration(item.durationSeconds)}
        </div>

        {/* Selection checkbox */}
        <div style={{
          position: 'absolute',
          top: 4,
          right: 4,
        }}>
          <input
            type="checkbox"
            checked={selected}
            onChange={e => { e.stopPropagation(); onToggleSelect() }}
            onClick={e => e.stopPropagation()}
            style={{ accentColor: 'var(--accent-primary)', cursor: 'pointer' }}
          />
        </div>
      </div>

      {/* Info */}
      <div style={{ padding: '0.55rem 0.65rem 0.6rem' }}>
        <div style={{
          fontSize: '0.8rem',
          fontWeight: 500,
          color: 'var(--text-primary)',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap',
          marginBottom: '0.2rem',
          fontFamily: 'var(--font-sans)',
        }}>
          {fileName}
        </div>
        <div style={{
          fontSize: '0.7rem',
          color: 'var(--text-dim)',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap',
          fontFamily: 'var(--font-mono)',
          marginBottom: '0.4rem',
        }}>
          {item.folder}
        </div>
        <div style={{
          display: 'flex',
          flexWrap: 'wrap',
          gap: '0.35rem',
          fontSize: '0.7rem',
        }}>
          {item.frameSize && (
            <MetaTag color="var(--text-muted)">{item.frameSize}</MetaTag>
          )}
          <MetaTag color={isBestBitrate ? 'var(--meta-best)' : 'var(--text-muted)'}>
            {formatBitrate(item.bitRateKbs)}
          </MetaTag>
          <MetaTag color={isBestSize ? 'var(--meta-worst)' : 'var(--text-muted)'}>
            {formatSize(item.sizeBytes)}
          </MetaTag>
          {item.fps > 0 && (
            <MetaTag color="var(--text-muted)">{item.fps.toFixed(0)}fps</MetaTag>
          )}
          {item.format && (
            <MetaTag color="var(--text-muted)">{item.format}</MetaTag>
          )}
        </div>
      </div>
    </div>
  )
}

function MetaTag({ children, color }: { children: React.ReactNode; color: string }) {
  return (
    <span style={{
      color,
      fontFamily: 'var(--font-mono)',
      fontSize: '0.65rem',
      background: color !== 'var(--text-muted)' ? 'rgba(255,255,255,0.04)' : 'transparent',
      padding: color !== 'var(--text-muted)' ? '0.1rem 0.35rem' : '0',
      borderRadius: 'var(--radius-sm)',
      border: color !== 'var(--text-muted)' ? '1px solid rgba(255,255,255,0.06)' : 'none',
    }}>
      {children}
    </span>
  )
}
