import { useState, useEffect, useCallback } from 'react'
import { ShoppingCart, ListTodo, Plus, Check, Trash2, Loader2 } from 'lucide-react'
import {
  fetchShoppingList,
  addShoppingItem,
  completeShoppingItem,
  removeShoppingItem,
  fetchTodoEntities,
  fetchTodoItems,
  addTodoItem,
  completeTodoItem,
  removeTodoItem,
} from '../api'
import type { ShoppingListItem, TodoItem, TodoEntitySummary } from '../api'

type Tab = 'shopping' | 'todo'

export default function ListsPage() {
  const [tab, setTab] = useState<Tab>('shopping')
  const [shoppingItems, setShoppingItems] = useState<ShoppingListItem[]>([])
  const [todoEntities, setTodoEntities] = useState<TodoEntitySummary[]>([])
  const [selectedTodoId, setSelectedTodoId] = useState<string>('')
  const [todoItems, setTodoItems] = useState<TodoItem[]>([])
  const [newItem, setNewItem] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [actionId, setActionId] = useState<string | null>(null)

  const loadShopping = useCallback(async () => {
    try {
      const items = await fetchShoppingList()
      setShoppingItems(items)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load shopping list')
    }
  }, [])

  const loadTodoEntities = useCallback(async () => {
    try {
      const entities = await fetchTodoEntities()
      setTodoEntities(entities)
      if (entities.length > 0 && !selectedTodoId) setSelectedTodoId(entities[0].entityId)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load todo lists')
    }
  }, [selectedTodoId])

  const loadTodoItems = useCallback(async () => {
    if (!selectedTodoId) return
    try {
      const items = await fetchTodoItems(selectedTodoId)
      setTodoItems(items)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load todo items')
    }
  }, [selectedTodoId])

  useEffect(() => {
    setLoading(true)
    setError(null)
    Promise.all([loadShopping(), loadTodoEntities()]).finally(() => setLoading(false))
  }, [loadShopping, loadTodoEntities])

  useEffect(() => {
    if (selectedTodoId) loadTodoItems()
  }, [selectedTodoId, loadTodoItems])

  const handleAddShopping = async () => {
    if (!newItem.trim()) return
    setActionId('add-shopping')
    setError(null)
    try {
      await addShoppingItem(newItem.trim())
      setNewItem('')
      await loadShopping()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to add item')
    } finally {
      setActionId(null)
    }
  }

  const handleAddTodo = async () => {
    if (!newItem.trim() || !selectedTodoId) return
    setActionId('add-todo')
    setError(null)
    try {
      await addTodoItem(selectedTodoId, newItem.trim())
      setNewItem('')
      await loadTodoItems()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to add item')
    } finally {
      setActionId(null)
    }
  }

  const handleCompleteShopping = async (name: string) => {
    setActionId(`complete-${name}`)
    try {
      await completeShoppingItem(name)
      await loadShopping()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to complete')
    } finally {
      setActionId(null)
    }
  }

  const handleRemoveShopping = async (name: string) => {
    setActionId(`remove-${name}`)
    try {
      await removeShoppingItem(name)
      await loadShopping()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to remove')
    } finally {
      setActionId(null)
    }
  }

  const handleCompleteTodo = async (item: string) => {
    if (!selectedTodoId) return
    setActionId(`complete-${item}`)
    try {
      await completeTodoItem(selectedTodoId, item)
      await loadTodoItems()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to complete')
    } finally {
      setActionId(null)
    }
  }

  const handleRemoveTodo = async (item: string) => {
    if (!selectedTodoId) return
    setActionId(`remove-${item}`)
    try {
      await removeTodoItem(selectedTodoId, item)
      await loadTodoItems()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to remove')
    } finally {
      setActionId(null)
    }
  }

  if (loading && shoppingItems.length === 0 && todoEntities.length === 0) {
    return (
      <div className="flex items-center justify-center py-16">
        <Loader2 className="h-8 w-8 animate-spin text-amber" />
      </div>
    )
  }

  return (
    <div>
      <div className="mb-6 flex items-center justify-between">
        <h1 className="font-display text-2xl font-bold text-light">Shopping & To-Do Lists</h1>
      </div>
      <p className="mb-6 text-sm text-dust">
        Manage your Home Assistant shopping list and todo lists. The Lists Agent can add items via voice or chat.
      </p>

      {error && (
        <div className="mb-4 rounded bg-ember/15 px-4 py-2 text-rose">
          {error}
          <button onClick={() => setError(null)} className="ml-2 hover:underline">Dismiss</button>
        </div>
      )}

      {/* Tabs */}
      <div className="mb-4 flex gap-2 border-b border-stone/40">
        <button
          onClick={() => setTab('shopping')}
          className={`flex items-center gap-2 rounded-t-lg px-4 py-2 text-sm font-medium transition-colors ${
            tab === 'shopping' ? 'border-b-2 border-amber bg-amber/10 text-amber' : 'text-fog hover:text-cloud'
          }`}
        >
          <ShoppingCart className="h-4 w-4" />
          Shopping List
        </button>
        <button
          onClick={() => setTab('todo')}
          className={`flex items-center gap-2 rounded-t-lg px-4 py-2 text-sm font-medium transition-colors ${
            tab === 'todo' ? 'border-b-2 border-amber bg-amber/10 text-amber' : 'text-fog hover:text-cloud'
          }`}
        >
          <ListTodo className="h-4 w-4" />
          To-Do Lists
        </button>
      </div>

      {tab === 'shopping' && (
        <div className="rounded border border-stone/40 bg-charcoal p-4">
          <div className="mb-4 flex gap-2">
            <input
              value={newItem}
              onChange={e => setNewItem(e.target.value)}
              onKeyDown={e => e.key === 'Enter' && handleAddShopping()}
              placeholder="Add item (e.g. milk, eggs)"
              className="flex-1 rounded border border-stone/60 bg-basalt px-3 py-2 text-sm text-light placeholder:text-dust"
            />
            <button
              onClick={handleAddShopping}
              disabled={!newItem.trim() || actionId === 'add-shopping'}
              className="rounded bg-amber px-4 py-2 text-sm font-medium text-void hover:bg-amber-glow disabled:opacity-50 disabled:cursor-not-allowed"
            >
              {actionId === 'add-shopping' ? <Loader2 className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />}
            </button>
          </div>
          {shoppingItems.length === 0 ? (
            <p className="text-dust">Shopping list is empty. Add items above or via the Lists Agent.</p>
          ) : (
            <ul className="space-y-2">
              {shoppingItems.map(item => (
                <li key={item.id} className={`flex items-center gap-3 rounded px-3 py-2 ${item.complete ? 'bg-stone/20' : 'bg-basalt/50'}`}>
                  <button
                    onClick={() => handleCompleteShopping(item.name)}
                    disabled={actionId !== null}
                    className={`rounded p-1 ${item.complete ? 'text-sage' : 'text-dust hover:text-sage'}`}
                  >
                    <Check className="h-4 w-4" />
                  </button>
                  <span className={item.complete ? 'flex-1 text-dust line-through' : 'flex-1'}>{item.name}</span>
                  <button
                    onClick={() => handleRemoveShopping(item.name)}
                    disabled={actionId !== null}
                    className="rounded p-1 text-dust hover:text-rose"
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      {tab === 'todo' && (
        <div className="rounded border border-stone/40 bg-charcoal p-4">
          {todoEntities.length === 0 ? (
            <p className="text-dust">No todo lists found. Add the Local todo integration in Home Assistant to create lists.</p>
          ) : (
            <>
              <div className="mb-4 flex flex-wrap items-center gap-3">
                <label className="text-sm text-dust">List:</label>
                <select
                  value={selectedTodoId}
                  onChange={e => setSelectedTodoId(e.target.value)}
                  className="rounded border border-stone/60 bg-basalt px-3 py-2 text-sm text-light"
                >
                  {todoEntities.map(e => (
                    <option key={e.entityId} value={e.entityId}>{e.name}</option>
                  ))}
                </select>
                <div className="flex flex-1 gap-2">
                  <input
                    value={newItem}
                    onChange={e => setNewItem(e.target.value)}
                    onKeyDown={e => e.key === 'Enter' && handleAddTodo()}
                    placeholder="Add task"
                    className="flex-1 min-w-0 rounded border border-stone/60 bg-basalt px-3 py-2 text-sm text-light placeholder:text-dust"
                  />
                  <button
                    onClick={handleAddTodo}
                    disabled={!newItem.trim() || actionId === 'add-todo'}
                    className="rounded bg-amber px-4 py-2 text-sm font-medium text-void hover:bg-amber-glow disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    {actionId === 'add-todo' ? <Loader2 className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />}
                  </button>
                </div>
              </div>
              {todoItems.length === 0 ? (
                <p className="text-dust">This list is empty.</p>
              ) : (
                <ul className="space-y-2">
                  {todoItems.map(item => (
                    <li key={item.uid} className={`flex items-center gap-3 rounded px-3 py-2 ${item.status === 'completed' ? 'bg-stone/20' : 'bg-basalt/50'}`}>
                      <button
                        onClick={() => handleCompleteTodo(item.summary)}
                        disabled={actionId !== null || item.status === 'completed'}
                        className={`rounded p-1 ${item.status === 'completed' ? 'text-sage' : 'text-dust hover:text-sage'}`}
                      >
                        <Check className="h-4 w-4" />
                      </button>
                      <span className={item.status === 'completed' ? 'flex-1 text-dust line-through' : 'flex-1'}>{item.summary}</span>
                      <button
                        onClick={() => handleRemoveTodo(item.summary)}
                        disabled={actionId !== null}
                        className="rounded p-1 text-dust hover:text-rose"
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </>
          )}
        </div>
      )}
    </div>
  )
}
