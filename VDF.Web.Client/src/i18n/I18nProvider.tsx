import React, { useState, useCallback, useMemo } from 'react'
import { I18nContext, type LanguageCode, type Translation } from './i18n'
import en from './en.json'
import zhHans from './zh-Hans.json'

const translations: Record<LanguageCode, Translation> = {
  'en': en,
  'zh-Hans': zhHans,
  'de': en,
  'es': en,
  'fr': en,
  'pt': en,
}

interface I18nProviderProps {
  children: React.ReactNode
  initialLang?: LanguageCode
}

export function I18nProvider({ children, initialLang = 'zh-Hans' }: I18nProviderProps) {
  const [lang, setLang] = useState<LanguageCode>(initialLang)

  const t = useCallback((key: string): string => {
    const translation = translations[lang]
    return translation[key] || translations['en'][key] || key
  }, [lang])

  const setLanguage = useCallback((newLang: LanguageCode) => {
    setLang(newLang)
    localStorage.setItem('vdf-lang', newLang)
  }, [])

  const contextValue = useMemo(() => ({
    lang,
    setLang: setLanguage,
    t,
  }), [lang, setLanguage, t])

  return (
    <I18nContext.Provider value={contextValue}>
      {children}
    </I18nContext.Provider>
  )
}