import { useState } from 'react'
import { useI18n } from '../i18n/i18n'

interface GuideStep {
  title: string
  description: string
  icon: string
}

const guideSteps: GuideStep[] = [
  {
    title: 'Welcome',
    description: 'Welcome to Video Duplicate Finder. This guide will help you get started.',
    icon: '👋',
  },
  {
    title: 'Select Folders',
    description: 'Start by selecting the folders containing your video and image files. You can add multiple folders to scan.',
    icon: '📁',
  },
  {
    title: 'Start Scan',
    description: 'Click the "Start Scan" button to begin searching for duplicate files. The scan may take some time depending on the number of files.',
    icon: '🔍',
  },
  {
    title: 'Review Results',
    description: 'After the scan completes, view your duplicate groups. You can compare files and mark them for deletion.',
    icon: '📊',
  },
  {
    title: 'Manage Duplicates',
    description: 'Use the results page to manage duplicates. You can delete unwanted files or move them to a different location.',
    icon: '🗑️',
  },
]

interface WelcomeGuideProps {
  onComplete: () => void
}

export function WelcomeGuide({ onComplete }: WelcomeGuideProps) {
  const { t } = useI18n()
  const [currentStep, setCurrentStep] = useState(0)
  const [completed, setCompleted] = useState(false)

  const current = guideSteps[currentStep]!

  const handleNext = () => {
    if (currentStep < guideSteps.length - 1) {
      setCurrentStep(currentStep + 1)
    } else {
      setCompleted(true)
    }
  }

  const handleSkip = () => {
    setCompleted(true)
  }

  if (completed || !current) {
    return (
      <div style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        minHeight: '60vh',
        padding: '2rem',
        animation: 'fadeInUp 0.4s ease both',
      }}>
        <div style={{ fontSize: '4rem', marginBottom: '1.5rem' }}>🎉</div>
        <h2 style={{
          fontFamily: 'var(--font-display)',
          fontSize: '1.8rem',
          fontWeight: 700,
          color: 'var(--text-primary)',
          margin: '0 0 0.75rem',
        }}>
          {t('Ready to Go!')}
        </h2>
        <p style={{
          fontSize: '0.95rem',
          color: 'var(--text-secondary)',
          textAlign: 'center',
          maxWidth: 400,
          marginBottom: '2rem',
        }}>
          {t('You are now ready to start finding duplicate videos and images.')}
        </p>
        <button
          onClick={onComplete}
          style={{
            padding: '0.7rem 2rem',
            borderRadius: 'var(--radius-lg)',
            border: 'none',
            background: 'linear-gradient(135deg, var(--accent-primary), #0284c7)',
            color: '#fff',
            cursor: 'pointer',
            fontSize: '0.9rem',
            fontWeight: 600,
            fontFamily: 'var(--font-sans)',
            display: 'flex',
            alignItems: 'center',
            gap: '0.5rem',
            boxShadow: '0 0 25px var(--accent-primary-glow), var(--shadow-md)',
            transition: 'all var(--transition-base)',
            letterSpacing: '0.03em',
          }}
        >
          {t('Start Scanning')}
        </button>
      </div>
    )
  }

  return (
    <div style={{
      maxWidth: 600,
      margin: '0 auto',
      padding: '2rem',
      animation: 'fadeInUp 0.4s ease both',
    }}>
      <div style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        marginBottom: '2.5rem',
      }}>
        <div style={{
          fontSize: '1.5rem',
          fontWeight: 700,
          fontFamily: 'var(--font-display)',
          color: 'var(--text-primary)',
        }}>
          {t('Getting Started')}
        </div>
        <button
          onClick={handleSkip}
          style={{
            padding: '0.4rem 1rem',
            border: '1px solid var(--border-input)',
            borderRadius: 'var(--radius-md)',
            background: 'transparent',
            color: 'var(--text-secondary)',
            cursor: 'pointer',
            fontSize: '0.8rem',
            fontFamily: 'var(--font-sans)',
            transition: 'all var(--transition-fast)',
          }}
        >
          {t('Skip')}
        </button>
      </div>

      <div style={{
        display: 'flex',
        justifyContent: 'center',
        gap: '0.5rem',
        marginBottom: '3rem',
      }}>
        {guideSteps.map((_, index) => (
          <div
            key={index}
            style={{
              width: currentStep >= index ? 20 : 8,
              height: 8,
              borderRadius: 4,
              background: currentStep === index ? 'var(--accent-primary)' : 'var(--border-default)',
              transition: 'all var(--transition-base)',
            }}
          />
        ))}
      </div>

      <div style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        textAlign: 'center',
        padding: '3rem 2rem',
        background: 'var(--bg-surface)',
        borderRadius: 'var(--radius-xl)',
        border: '1px solid var(--border-default)',
        boxShadow: 'var(--shadow-md)',
      }}>
        <div style={{
          fontSize: '4.5rem',
          marginBottom: '1.75rem',
          animation: 'bounce 1s ease infinite',
        }}>
          {current.icon}
        </div>
        <h3 style={{
          fontFamily: 'var(--font-display)',
          fontSize: '1.4rem',
          fontWeight: 600,
          color: 'var(--text-primary)',
          margin: '0 0 0.75rem',
        }}>
          {t(current.title)}
        </h3>
        <p style={{
          fontSize: '0.95rem',
          color: 'var(--text-secondary)',
          lineHeight: '1.6',
          maxWidth: 400,
        }}>
          {t(current.description)}
        </p>
      </div>

      <div style={{
        display: 'flex',
        justifyContent: 'flex-end',
        marginTop: '2rem',
      }}>
        <button
          onClick={handleNext}
          style={{
            padding: '0.65rem 2rem',
            borderRadius: 'var(--radius-lg)',
            border: 'none',
            background: 'linear-gradient(135deg, var(--accent-primary), #0284c7)',
            color: '#fff',
            cursor: 'pointer',
            fontSize: '0.88rem',
            fontWeight: 600,
            fontFamily: 'var(--font-sans)',
            display: 'flex',
            alignItems: 'center',
            gap: '0.5rem',
            boxShadow: '0 0 20px var(--accent-primary-glow), var(--shadow-sm)',
            transition: 'all var(--transition-base)',
            letterSpacing: '0.03em',
          }}
        >
          {currentStep < guideSteps.length - 1 ? t('Next') : t('Finish')}
          <span>→</span>
        </button>
      </div>

      <style>{`
        @keyframes bounce {
          0%, 100% { transform: translateY(0); }
          50% { transform: translateY(-10px); }
        }
      `}</style>
    </div>
  )
}
