import { describe, expect, it } from 'vitest'
import { mentionName } from './mentions'

describe('mentions', () => {
  it('searches a declaration by its own name, the last segment of a qualified one', () => {
    expect(mentionName({ id: '_on_body_entered', name: '_on_body_entered', kind: 'function' })).toBe('_on_body_entered')
    expect(mentionName({ id: 'BoardTools.ShowAsync', name: 'ShowAsync', kind: 'method' })).toBe('ShowAsync')
    expect(mentionName({ id: 'PLAYERS.ann.move', name: 'PLAYERS.ann.move', kind: 'method' })).toBe('move')
    expect(mentionName({ id: 'SPEED', name: 'SPEED', kind: 'constant' })).toBe('SPEED')
  })

  it('has nothing to search for parts without a name of their own', () => {
    expect(mentionName({ id: null, name: 'constants', kind: 'constants' })).toBeNull()
    expect(mentionName({ id: null, name: 'moving', kind: 'section' })).toBeNull()
    expect(mentionName({ id: 'milestones', name: 'Milestones', kind: 'section' })).toBeNull()
    expect(mentionName({ id: 'players.move', name: 'move', kind: 'test' })).toBeNull()
    expect(mentionName({ id: '(operator)', name: '(operator)', kind: 'operator' })).toBeNull()
    expect(mentionName({ id: 'x', name: 'x', kind: 'variable' })).toBeNull()
  })
})
