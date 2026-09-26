// A dedicated clock decouples capture from the browser paint/visibility loop.
let timer;
onmessage = ({data}) => {
  clearInterval(timer);
  if (data === 'start') timer = setInterval(() => postMessage('frame'), 33);
};
