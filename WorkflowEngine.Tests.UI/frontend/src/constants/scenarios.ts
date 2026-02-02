export const SCENARIOS = [
  { id: 'demo_chat', name: 'Demo Chat', description: 'Simple echo chat for UI testing' },
  { id: 'ai_chat', name: 'AI Chat', description: 'Chat with AI (OpenAI)' },
] as const

export type ScenarioId = (typeof SCENARIOS)[number]['id']
