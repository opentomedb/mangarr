import React from 'react';
import styles from './LoadingMessage.css';

// Manga / light-novel flavoured (the maintainer, 2026-09-19); one is picked per page load.
const messages = [
  'Waiting for the next chapter...',
  'Skipping the recap episode...',
  'Reading right to left...',
  'Translating the raws...',
  'Summoning the scanlation team...',
  'This is not a filler arc, we promise',
  'Powering up... this may take a few episodes',
  'Waiting for the sub group to release...',
  'Isekai\'d into the loading screen',
  'Loading in 4K... just kidding, 480p fansub',
  'Previously on Mangarr...',
  'Cliffhanger! To be continued in the next volume',
  'Turning the page... the other way',
  'Redrawing the SFX by hand',
  'The light novel is longer, please wait',
  'Typesetting the speech bubbles...',
  'Omake loading...',
  'Sensei, the deadline was yesterday'
];

let message = null;

function LoadingMessage() {
  if (!message) {
    const index = Math.floor(Math.random() * messages.length);
    message = messages[index];
  }

  return (
    <div className={styles.loadingMessage}>
      {message}
    </div>
  );
}

export default LoadingMessage;
