import { createContext, useContext } from 'react'
import en from './en.json'
import zhHans from './zh-Hans.json'

export type LanguageCode = 'en' | 'zh-Hans' | 'de' | 'es' | 'fr' | 'pt'

export interface Translation {
  [key: string]: string
}

const translations: Record<LanguageCode, Translation> = {
  'en': en,
  'zh-Hans': zhHans,
  'de': en,
  'es': en,
  'fr': en,
  'pt': en,
}

export const availableLanguages: { code: LanguageCode; name: string }[] = [
  { code: 'en', name: 'English' },
  { code: 'zh-Hans', name: '中文 (简体)' },
  { code: 'de', name: 'Deutsch' },
  { code: 'es', name: 'Español' },
  { code: 'fr', name: 'Français' },
  { code: 'pt', name: 'Português' },
]

export interface I18nContextType {
  lang: LanguageCode
  setLang: (lang: LanguageCode) => void
  t: (key: string) => string
}

export const I18nContext = createContext<I18nContextType | undefined>(undefined)

export function useI18n() {
  const context = useContext(I18nContext)
  if (!context) {
    throw new Error('useI18n must be used within an I18nProvider')
  }
  return context
}

export function createI18n(lang: LanguageCode): I18nContextType {
  const t = (key: string): string => {
    const translation = translations[lang]
    return translation[key] || translations['en'][key] || key
  }

  return {
    lang,
    setLang: () => {},
    t,
  }
}