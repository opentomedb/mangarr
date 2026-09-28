import { routerMiddleware } from 'connected-react-router';
import { applyMiddleware, compose } from 'redux';
import thunk from 'redux-thunk';
import createPersistState from './createPersistState';

export default function(history) {
  // Beta readiness (2026-09-28): no Sentry middleware -- it reported reducer errors, with the
  // whole store attached, to Servarr's Sentry project.
  const middlewares = [];

  middlewares.push(routerMiddleware(history));
  middlewares.push(thunk);

  // eslint-disable-next-line no-underscore-dangle
  const composeEnhancers = window.__REDUX_DEVTOOLS_EXTENSION_COMPOSE__ || compose;

  return composeEnhancers(
    applyMiddleware(...middlewares),
    createPersistState()
  );
}
